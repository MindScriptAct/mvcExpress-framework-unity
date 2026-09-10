using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;
using mvcExpress;

namespace mvcExpress.Tests
{
    // Regression coverage: ModuleInitializer.InitializeCodeRegisteredServicesFromContainer
    // deduplicated dual-scope registrations with `new HashSet<object>()` (default equality), and
    // separately checked `_attributeRegisteredServices.Contains(instance)` (also default equality).
    // Two DISTINCT code-registered services whose type overrides Equals/GetHashCode collide on
    // either check, so the second one encountered is silently skipped: never injected, never
    // OnInitialized(), never added to the lifecycle-cleanup list - a live, resolvable instance left
    // half-initialized.
    [TestFixture]
    public class ModuleInitializer_ReferenceIdentityDedup_UnitTests
    {
        private interface IValueEqualMarker { }

        // Overrides value equality so two distinct instances with the same Key compare equal.
        private sealed class ValueEqualLifecycleService : IMvcLifecycle, IValueEqualMarker
        {
            public readonly int Key;
            public bool OnInitializedCalled { get; private set; }

            public ValueEqualLifecycleService(int key) => Key = key;

            public void OnInitialized() => OnInitializedCalled = true;
            public void OnCleanup() { }

            public override bool Equals(object obj) => obj is ValueEqualLifecycleService other && other.Key == Key;
            public override int GetHashCode() => Key;
        }

        // Registers two distinct, value-equal services under two different container keys (one
        // concrete-type, one interface) so the global-type-identity guard does not block the
        // setup - only their VALUE equality collides, not their container keys.
        private sealed class TwoValueEqualServicesModule : MvcModule
        {
            public ValueEqualLifecycleService First { get; private set; }
            public ValueEqualLifecycleService Second { get; private set; }

            protected override void RegisterServices()
            {
                First = new ValueEqualLifecycleService(1);
                Second = new ValueEqualLifecycleService(1); // distinct instance, same Key

                Container.Register(First).ToLogic().AsPermanent();
                Container.Register(Second).ToLogicAs<IValueEqualMarker>().AsPermanent();
            }
        }

        private static (GameObject go, T module) CreateModule<T>() where T : MvcModule
        {
            var go = new GameObject(typeof(T).Name);
            var module = go.AddComponent<T>();
            return (go, module);
        }

        private static void DestroyModuleAndFacade(GameObject moduleGo)
        {
            if (moduleGo != null)
                Object.DestroyImmediate(moduleGo);

            var facade = MvcFacade.InstanceOrNull;
            if (facade != null)
                Object.DestroyImmediate(facade.gameObject);
        }

        [Test]
        public void TwoValueEqualCodeServices_BothInjectedAndInitialized()
        {
            var (go, module) = CreateModule<TwoValueEqualServicesModule>();
            try
            {
                Assert.That(module.First, Is.EqualTo(module.Second),
                    "Precondition: the two registered instances must be value-equal.");
                Assert.That(ReferenceEquals(module.First, module.Second), Is.False,
                    "Precondition: distinct instances.");

                Assert.That(module.First.OnInitializedCalled, Is.True,
                    "The first-enumerated of two value-equal code-registered services must be initialized.");
                Assert.That(module.Second.OnInitializedCalled, Is.True,
                    "The second-enumerated of two value-equal code-registered services must ALSO be " +
                    "initialized - it must not be silently skipped just because a distinct, value-equal " +
                    "instance was already processed this loop.");
            }
            finally
            {
                DestroyModuleAndFacade(go);
            }
        }
    }
}
