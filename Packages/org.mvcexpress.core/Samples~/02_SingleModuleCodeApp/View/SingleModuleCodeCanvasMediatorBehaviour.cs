using mvcExpress;
using System.Collections;
using UnityEngine;

namespace mvcExpress.Samples.SingleModuleCodeApp.View
{
    // Root view mediator. Owns the Canvas prefab and attaches child view prefabs from code.
    public sealed class SingleModuleCodeCanvasMediatorBehaviour : MediatorBehaviour
    {
        [SerializeField] private Transform _hudContainer;

        protected override void OnInitialized()
        {
            StartCoroutine(AttachHudAfterModuleInitialization());
        }

        private IEnumerator AttachHudAfterModuleInitialization()
        {
            // Child prefab attachment must happen after the module's startup mediator batch is complete.
            yield return null;

            var parent = _hudContainer != null ? _hudContainer : transform;
            MediatorHub.AttachPrefab<SingleModuleCodeHudMediatorBehaviour>(parent);
        }
    }
}
