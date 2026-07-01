using mvcExpress.Editor.Generators;
using UnityEditor;
using UnityEngine;

namespace mvcExpress.Editor
{
    internal static class MvcHierarchyScaffoldingMenu
    {
        private const int PriorityBase = 10;

        // ── Actor component generators (11-13) ───────────────────────────────

        [MenuItem("GameObject/mvcExpress/MediatorBehaviour", false, PriorityBase + 1)]
        private static void CreateMediator(MenuCommand cmd) =>
            MvcMediatorBehaviourGeneratorWindow.ShowWindow(cmd.context as GameObject);

        [MenuItem("GameObject/mvcExpress/ProxyBehaviour", false, PriorityBase + 2)]
        private static void CreateProxyBehaviour(MenuCommand cmd) =>
            MvcProxyBehaviourGeneratorWindow.ShowWindow(cmd.context as GameObject);

        [MenuItem("GameObject/mvcExpress/ServiceBehaviour", false, PriorityBase + 3)]
        private static void CreateServiceBehaviour(MenuCommand cmd) =>
            MvcServiceBehaviourGeneratorWindow.ShowWindow(cmd.context as GameObject);

        // ── Structure generators (40-41) ─────────────────────────────────────

        [MenuItem("GameObject/mvcExpress/Module", false, PriorityBase + 30)]
        private static void CreateModule(MenuCommand cmd) =>
            MvcModuleHierarchyGeneratorWindow.ShowWindow(cmd.context as GameObject);

        [MenuItem("GameObject/mvcExpress/MvcFacade", false, PriorityBase + 31)]
        private static void CreateMvcFacade(MenuCommand cmd)
        {
            var go = new GameObject(nameof(MvcFacade));
            GameObjectUtility.SetParentAndAlign(go, cmd.context as GameObject);
            Undo.RegisterCreatedObjectUndo(go, "Create MvcFacade");
            go.AddComponent<MvcFacade>();
            Selection.activeGameObject = go;
        }

        // ── Mediator tools (61) ───────────────────────────────────────────────
        // Auto-separator from the group above (priority gap > 10).
        //
        // Unity limitation: in the Hierarchy right-click context menu, a validate
        // returning false HIDES the item — there is no API to show it grayed.
        // Validate only checks for any selection (so the item stays visible); the
        // action then handles the no-mediator case with an explanatory dialog.

        [MenuItem("GameObject/mvcExpress/Generate Mediator Handlers...", false, PriorityBase + 51)]
        private static void GenerateMediatorHandlers(MenuCommand cmd)
        {
            var go = cmd.context as GameObject ?? Selection.activeGameObject;
            if (go == null) return;

            if (go.GetComponent<MediatorBehaviour>() == null)
            {
                EditorUtility.DisplayDialog(
                    "MediatorBehaviour Required",
                    $"'{go.name}' does not have a MediatorBehaviour component.\n\n" +
                    "Attach a MediatorBehaviour script to this GameObject first.",
                    "OK");
                return;
            }

            MvcMediatorViewTriggerGeneratorWindow.ShowWindow(go);
        }

        [MenuItem("GameObject/mvcExpress/Generate Mediator Handlers...", true)]
        private static bool ValidateGenerateMediatorHandlers() =>
            Selection.activeGameObject != null;
    }
}
