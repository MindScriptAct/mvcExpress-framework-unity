using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace mvcExpress.Tests.Scenarios
{
    // Regression coverage: [Attach(FindInScene = true)] used to resolve via
    // a GLOBAL scene search (FindAnyObjectByType/FindObjectOfType), so in a multi-module scene it
    // could attach a mediator belonging to a DIFFERENT module, and candidate choice among several
    // scene instances was non-deterministic. Now scoped to the owning module's own hierarchy via
    // AttributeMonoBehaviourResolver - the same hierarchy-scoped, fail-on-ambiguity mechanism already
    // used for [Register] Proxy/Service attribute attachment - instead of a scene-wide search.
    [TestFixture]
    [Category("Scenario")]
    public class MediatorFindInScene_HierarchyScope_ScenarioTests
    {
        public sealed class OwningModule : MvcModule { }
        public sealed class AmbiguousModule : MvcModule { }

        [Attach(typeof(OwningModule), FindInScene = true)]
        public sealed class ScopedMediator : MediatorBehaviour
        {
            public static int AttachCount;
            protected override void OnInitialized() => AttachCount++;
        }

        [Attach(typeof(AmbiguousModule), FindInScene = true)]
        public sealed class AmbiguousMediator : MediatorBehaviour
        {
            public static int AttachCount;
            protected override void OnInitialized() => AttachCount++;
        }

        private readonly List<GameObject> _createdGameObjects = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            ScopedMediator.AttachCount = 0;
            AmbiguousMediator.AttachCount = 0;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _createdGameObjects)
            {
                if (go != null) Object.DestroyImmediate(go);
            }
            _createdGameObjects.Clear();

            var facade = MvcFacade.InstanceOrNull;
            if (facade != null) Object.DestroyImmediate(facade.gameObject);
        }

        private GameObject CreateGameObject(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent);
            _createdGameObjects.Add(go);
            return go;
        }

        [UnityTest]
        public IEnumerator FindInScene_InstanceUnderUnrelatedHierarchy_IsNotAttachedToOwningModule()
        {
            // Hand-placed under an UNRELATED part of the scene (representing another module's own
            // hierarchy), not under OwningModule's transform at all.
            var otherRoot = CreateGameObject("UnrelatedModuleRoot");
            var mediatorGo = CreateGameObject("Mediator", otherRoot.transform);
            mediatorGo.AddComponent<ScopedMediator>();

            var moduleGo = CreateGameObject(nameof(OwningModule));

            LogAssert.Expect(LogType.Error, new Regex("ScopedMediator"));
            LogAssert.Expect(LogType.Error, new Regex("failed to initialize and will be destroyed"));
            LogAssert.Expect(LogType.Exception, new Regex("attribute composition error"));

            try
            {
                moduleGo.AddComponent<OwningModule>();
            }
            catch (System.Exception)
            {
                // AddComponent may or may not surface the exception depending on Unity version;
                // either way the module has already self-destroyed by the time this returns.
            }

            yield return null;

            Assert.That(ScopedMediator.AttachCount, Is.EqualTo(0),
                "A mediator instance hand-placed outside the owning module's own hierarchy must not " +
                "be attached to that module - a global scene search used to attach whichever instance " +
                "FindObjectOfType happened to return, regardless of which hierarchy it actually lived in.");
            Assert.IsTrue(moduleGo == null,
                "A module whose required mediator exists only outside its hierarchy must fail composition " +
                "and be destroyed, not continue running without the mediator.");
        }

        [Test]
        public void FindInScene_InstanceUnderOwningModule_IsFoundAndAttached()
        {
            var moduleGo = CreateGameObject(nameof(OwningModule));
            var mediatorGo = CreateGameObject("Mediator", moduleGo.transform);
            mediatorGo.AddComponent<ScopedMediator>();

            moduleGo.AddComponent<OwningModule>();

            Assert.That(ScopedMediator.AttachCount, Is.EqualTo(1),
                "A mediator instance hand-placed as a descendant of the owning module's own " +
                "transform must be found and attached exactly once.");
        }

        [UnityTest]
        public IEnumerator FindInScene_TwoInstancesUnderSameModule_ThrowsAndDestroysModule()
        {
            // Ambiguous FindInScene resolution is a fatal composition failure (promoted from
            // log-and-skip): the module runs its full initialization pass, records the failure,
            // then throws one aggregated MvcCompositionException, which MvcModule.Awake() turns
            // into a self-destroy - same fatal path as RegisterAttributeStackingScenarioTests'
            // duplicate-mapping and lifecycle-conflict tests.
            var moduleGo = CreateGameObject(nameof(AmbiguousModule));
            var first = CreateGameObject("First", moduleGo.transform);
            first.AddComponent<AmbiguousMediator>();
            var second = CreateGameObject("Second", moduleGo.transform);
            second.AddComponent<AmbiguousMediator>();

            LogAssert.Expect(LogType.Error, new Regex("AmbiguousMediator"));
            LogAssert.Expect(LogType.Error, new Regex("failed to initialize and will be destroyed"));
            LogAssert.Expect(LogType.Exception, new Regex("attribute composition error"));

            try
            {
                moduleGo.AddComponent<AmbiguousModule>();
            }
            catch (System.Exception)
            {
                // AddComponent may or may not surface the exception depending on Unity version;
                // either way the module has already self-destroyed by the time this returns.
            }

            // Destroy(gameObject) is deferred to end-of-frame in Play Mode - yield once so it
            // actually takes effect before checking Unity's overloaded null comparison.
            yield return null;

            Assert.That(AmbiguousMediator.AttachCount, Is.EqualTo(0),
                "Two candidate instances in the same module's hierarchy must be reported as " +
                "ambiguous and neither attached - not resolved arbitrarily to whichever one a scene " +
                "search happens to return first.");
            Assert.IsTrue(moduleGo == null,
                "A module with an unresolved ambiguous mediator must be destroyed, not left running " +
                "with the conflict silently unresolved.");
        }

        [UnityTest]
        public IEnumerator FindInScene_NoInstanceAnywhere_ThrowsAndDestroysModule()
        {
            // Promoted from LogWarning/no-throw to a fatal composition failure: a
            // [Attach(FindInScene = true)] declaration is a claim that the mediator belongs in this
            // module's hierarchy, so its absence is treated as a missing/misconfigured scene rather
            // than an intentionally optional one.
            var moduleGo = CreateGameObject(nameof(OwningModule));

            LogAssert.Expect(LogType.Error, new Regex("ScopedMediator"));
            LogAssert.Expect(LogType.Error, new Regex("failed to initialize and will be destroyed"));
            LogAssert.Expect(LogType.Exception, new Regex("attribute composition error"));

            try
            {
                moduleGo.AddComponent<OwningModule>();
            }
            catch (System.Exception)
            {
                // AddComponent may or may not surface the exception depending on Unity version;
                // either way the module has already self-destroyed by the time this returns.
            }

            yield return null;

            Assert.That(ScopedMediator.AttachCount, Is.EqualTo(0));
            Assert.IsTrue(moduleGo == null,
                "A module declaring a mediator via [Attach(FindInScene = true)] that isn't anywhere " +
                "in its hierarchy must be destroyed, not left running without it.");
        }
    }
}
