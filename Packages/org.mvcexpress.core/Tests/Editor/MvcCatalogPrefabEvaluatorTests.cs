using NUnit.Framework;
using mvcExpress.Editor.Core;
using System.Collections.Generic;
using UnityEngine;

namespace mvcExpress.Editor.Tests
{
    public class MvcCatalogPrefabEvaluatorTests
    {
        private GameObject _prefabA;
        private GameObject _prefabB;

        [SetUp]
        public void SetUp()
        {
            _prefabA = new GameObject("PrefabA");
            _prefabB = new GameObject("PrefabB");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_prefabA);
            Object.DestroyImmediate(_prefabB);
        }

        [Test]
        public void Evaluate_SingleCandidate_ProducesValidMapping()
        {
            var candidates = new Dictionary<System.Type, List<GameObject>>
            {
                { typeof(string), new List<GameObject> { _prefabA } }
            };

            var result = MvcCatalogPrefabEvaluator.Evaluate(candidates);

            Assert.That(result.Errors, Is.Empty);
            Assert.That(result.ValidMappings, Has.Count.EqualTo(1));
            Assert.That(result.ValidMappings[0].MediatorType, Is.EqualTo(typeof(string)));
            Assert.That(result.ValidMappings[0].Prefab, Is.EqualTo(_prefabA));
        }

        [Test]
        public void Evaluate_NoCandidates_ProducesOrphanError()
        {
            var candidates = new Dictionary<System.Type, List<GameObject>>
            {
                { typeof(string), new List<GameObject>() }
            };

            var result = MvcCatalogPrefabEvaluator.Evaluate(candidates);

            Assert.That(result.ValidMappings, Is.Empty);
            Assert.That(result.Errors, Has.Count.EqualTo(1));
            Assert.That(result.Errors[0], Does.Contain("no prefab"));
        }

        [Test]
        public void Evaluate_MultipleCandidates_ProducesDuplicateError()
        {
            var candidates = new Dictionary<System.Type, List<GameObject>>
            {
                { typeof(string), new List<GameObject> { _prefabA, _prefabB } }
            };

            var result = MvcCatalogPrefabEvaluator.Evaluate(candidates);

            Assert.That(result.ValidMappings, Is.Empty);
            Assert.That(result.Errors, Has.Count.EqualTo(1));
            Assert.That(result.Errors[0], Does.Contain("multiple prefabs"));
            Assert.That(result.HasErrors, Is.True);
        }

        [Test]
        public void Evaluate_NullDictionary_ReturnsEmptyResult()
        {
            var result = MvcCatalogPrefabEvaluator.Evaluate(null);

            Assert.That(result.ValidMappings, Is.Empty);
            Assert.That(result.Errors, Is.Empty);
        }

        [Test]
        public void Evaluate_MixedValidAndInvalid_ReportsBoth()
        {
            var candidates = new Dictionary<System.Type, List<GameObject>>
            {
                { typeof(string), new List<GameObject> { _prefabA } },
                { typeof(int), new List<GameObject>() }
            };

            var result = MvcCatalogPrefabEvaluator.Evaluate(candidates);

            Assert.That(result.ValidMappings, Has.Count.EqualTo(1));
            Assert.That(result.Errors, Has.Count.EqualTo(1));
        }
    }
}
