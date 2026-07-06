using NUnit.Framework;
using mvcExpress;
using mvcExpress.Editor.Core;
using System.Collections.Generic;
using UnityEngine;

namespace mvcExpress.Editor.Tests
{
    public class MvcCatalogPrefabWriterTests
    {
        private ViewPrefabCatalog _catalog;
        private GameObject _prefab;

        [SetUp]
        public void SetUp()
        {
            _catalog = ScriptableObject.CreateInstance<ViewPrefabCatalog>();
            _prefab = new GameObject("Prefab");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_catalog);
            Object.DestroyImmediate(_prefab);
        }

        [Test]
        public void Write_SingleMapping_PopulatesCatalog()
        {
            var mappings = new List<CatalogPrefabMapping> { new CatalogPrefabMapping(typeof(string), _prefab) };

            MvcCatalogPrefabWriter.Write(_catalog, mappings);

            Assert.That(_catalog.MediatorPrefabs, Has.Length.EqualTo(1));
            Assert.That(_catalog.MediatorPrefabs[0].MediatorTypeName, Is.EqualTo(typeof(string).AssemblyQualifiedName));
            Assert.That(_catalog.MediatorPrefabs[0].Prefab, Is.EqualTo(_prefab));
        }

        [Test]
        public void Write_EmptyMappings_ClearsCatalog()
        {
            MvcCatalogPrefabWriter.Write(_catalog, new List<CatalogPrefabMapping> { new CatalogPrefabMapping(typeof(string), _prefab) });
            MvcCatalogPrefabWriter.Write(_catalog, new List<CatalogPrefabMapping>());

            Assert.That(_catalog.MediatorPrefabs, Is.Empty);
        }

        [Test]
        public void Write_NullCatalog_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => MvcCatalogPrefabWriter.Write(null, new List<CatalogPrefabMapping>()));
        }

        [Test]
        public void Write_OverwritesPreviousContents()
        {
            var otherPrefab = new GameObject("Other");
            try
            {
                MvcCatalogPrefabWriter.Write(_catalog, new List<CatalogPrefabMapping> { new CatalogPrefabMapping(typeof(int), otherPrefab) });
                MvcCatalogPrefabWriter.Write(_catalog, new List<CatalogPrefabMapping> { new CatalogPrefabMapping(typeof(string), _prefab) });

                Assert.That(_catalog.MediatorPrefabs, Has.Length.EqualTo(1));
                Assert.That(_catalog.MediatorPrefabs[0].MediatorTypeName, Is.EqualTo(typeof(string).AssemblyQualifiedName));
            }
            finally
            {
                Object.DestroyImmediate(otherPrefab);
            }
        }
    }
}
