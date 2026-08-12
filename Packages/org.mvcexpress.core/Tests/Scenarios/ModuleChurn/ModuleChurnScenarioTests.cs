using System;
using System.Reflection;
using mvcExpress.Internal.Commands;
using mvcExpress.Internal.DependencyInjection;
using mvcExpress.Internal.Messaging;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace mvcExpress.Tests.Scenarios
{
    /// <summary>
    /// Simulates a game returning to the main menu repeatedly: create a module with a bound
    /// pooled command, drive one MVC loop, destroy it, repeat. Guards against static-storage
    /// leaks across module churn (implementation audit H1, H4).
    /// </summary>
    [TestFixture]
    [Category("Scenario")]
    public class ModuleChurnScenarioTests
    {
        private const int ChurnIterations = 20;

        private sealed class NoInitModule : MvcModule
        {
            protected override void Awake() { }
            protected override void OnDestroy() { }
        }

        private readonly struct ChurnMessage : IMessage { }

        private sealed class ChurnCommand : Command
        {
            public static int ExecuteCount;

            // Tracks the exact instance most recently executed, mirroring the static-tracking-field
            // idiom used by TransientLifecycleScenarioTests' UsesTransientProxyCommand. Needed so the
            // second test below can capture a WeakReference to the actual instance Publish executed
            // (and the pool retained), rather than a separate instance obtained via a later Get() call.
            public static ChurnCommand LastExecuted;

            public override void Execute()
            {
                ExecuteCount++;
                LastExecuted = this;
            }
        }

        [SetUp]
        public void SetUp()
        {
            ChurnCommand.ExecuteCount = 0;
            ChurnCommand.LastExecuted = null;
        }

        [Test]
        public void RepeatedModuleCreateDestroy_StaticCommandPoolSlots_DoNotGrowUnbounded()
        {
            var poolsField = GetStaticPoolsField();
            int? poolsLengthAfterFirstCycle = null;

            for (int i = 0; i < ChurnIterations; i++)
            {
                var go = new GameObject("ChurnModule_" + i);
                var module = go.AddComponent<NoInitModule>();
                var container = new MvcDiContainer();
                var bus = new MvcMessageBus();
                var processor = new MvcCommandProcessor(typeof(NoInitModule), container, bus, module);

                processor.BindCommand<ChurnCommand, ChurnMessage>();
                bus.Publish<ChurnMessage>();

                processor.Dispose();
                bus.Dispose();
                container.Dispose();
                Object.DestroyImmediate(go);

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                if (i == 0)
                {
                    poolsLengthAfterFirstCycle = ((Array)poolsField.GetValue(null)).Length;
                }
            }

            Assert.That(ChurnCommand.ExecuteCount, Is.EqualTo(ChurnIterations),
                "Precondition: every iteration's command must actually have executed once.");

            var poolsLengthAfterChurn = ((Array)poolsField.GetValue(null)).Length;
            Assert.That(poolsLengthAfterChurn, Is.EqualTo(poolsLengthAfterFirstCycle),
                "CommandPool<ChurnCommand>.Pools must not keep growing across module churn. Before the " +
                "H1 fix, processor instance IDs were never recycled, so every create/destroy cycle " +
                "permanently leaked one static array slot (and the pooled command instance in it).");
        }

        [Test]
        public void RepeatedModuleCreateDestroy_PooledCommandInstances_BecomeCollectable()
        {
            WeakReference lastCommandRef = null;

            for (int i = 0; i < ChurnIterations; i++)
            {
                bool captureRef = i == ChurnIterations - 1;
                lastCommandRef = RunOneChurnCycle(i, captureRef);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            Assert.That(lastCommandRef, Is.Not.Null, "Precondition: a pooled command reference must have been captured.");
            Assert.That(lastCommandRef.IsAlive, Is.False,
                "A pooled command from the final iteration's module must become collectable after that " +
                "module's processor is disposed. Before the H1 fix, Dispose() never called pool.Clear() " +
                "and never nulled the static slot, so the pooled command (and everything it held: module " +
                "context, DI container, injected references) stayed reachable through static storage " +
                "for the lifetime of the process.");
        }

        /// <summary>
        /// Runs one create/publish/dispose cycle in its own stack frame (see
        /// <see cref="System.Runtime.CompilerServices.MethodImplOptions.NoInlining"/> below). Without
        /// this isolation, an unoptimized/Debug JIT can keep this method's locals (e.g. <c>module</c>,
        /// <c>processor</c>) rooted in the calling test method's stack frame for the frame's entire
        /// lifetime rather than just this cycle's, which would make the captured WeakReference look
        /// artificially alive regardless of whether Dispose() actually released it - the same
        /// technique already used by WeakEventManager_UnitTests.SubscribeDeadHandler in this suite.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static WeakReference RunOneChurnCycle(int iteration, bool captureRef)
        {
            var go = new GameObject("ChurnModule_" + iteration);
            var module = go.AddComponent<NoInitModule>();
            var container = new MvcDiContainer();
            var bus = new MvcMessageBus();
            var processor = new MvcCommandProcessor(typeof(NoInitModule), container, bus, module);

            // poolSize: 1 is essential here: with the default poolSize of 0, the underlying
            // BoundedObjectPool is created with maxSize == 0, which means Return() disposes and
            // discards every instance immediately instead of pushing it onto the pool's retained
            // stack. That would make CommandPool<TCommand>.Pools hold no live reference at all, so
            // this test would pass regardless of whether Dispose() correctly clears the pool - it
            // wouldn't be testing the H1 fix. With poolSize: 1, the executed instance is actually
            // retained by the pool's internal stack (and by the static Pools[] slot), matching how a
            // real pooled command would be held in a running game.
            processor.BindCommand<ChurnCommand, ChurnMessage>(poolSize: 1);
            bus.Publish<ChurnMessage>();

            WeakReference weakRef = null;
            if (captureRef)
            {
                // Capture a WeakReference to the exact instance Publish just executed and returned
                // to the pool - not a second, freshly-minted instance obtained via a separate
                // Get()/Return() round-trip afterward (which would never be reachable through the
                // processor's static storage and so could never expose the H1 bug).
                weakRef = new WeakReference(ChurnCommand.LastExecuted);
            }

            // Clear the static reference so the test itself does not root the instance; only the
            // processor's static CommandPool<TCommand>.Pools slot (or lack thereof, after a correct
            // Dispose) should determine whether it is still reachable.
            ChurnCommand.LastExecuted = null;

            processor.Dispose();
            bus.Dispose();
            container.Dispose();
            Object.DestroyImmediate(go);

            return weakRef;
        }

        private static FieldInfo GetStaticPoolsField()
        {
            var processorType = typeof(MvcCommandProcessor);
            var commandPoolOpenType = processorType.GetNestedType("CommandPool`1", BindingFlags.NonPublic);
            Assert.That(commandPoolOpenType, Is.Not.Null,
                "MvcCommandProcessor.CommandPool<TCommand> nested type must exist - update this reflection helper if it was renamed.");
            var closedType = commandPoolOpenType.MakeGenericType(typeof(ChurnCommand));
            var field = closedType.GetField("Pools", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(field, Is.Not.Null, "CommandPool<TCommand>.Pools field must exist.");
            return field;
        }
    }
}
