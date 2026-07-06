using mvcExpress.Editor.Generators;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace mvcExpress.Editor
{
    /// <summary>
    /// Simple scaffolding helpers to create mvcExpress actors/messages.
    /// Generated scripts contain small guidance comments for learning MVC usage.
    /// </summary>
    public static class MvcProjectScaffoldingMenu
    {
        private const string DefaultNamespace = "myApp";

        private const int BasePriority = 900;

        [MenuItem("Assets/mvcExpress/Generate/Module Structure")]
        private static void OpenModuleStructureGenerator()
        {
            MvcModuleStructureGeneratorWindow.ShowWindow();
        }

        [MenuItem("Assets/mvcExpress/Create/Command", priority = BasePriority)]
        private static void OpenCommandGenerator()
        {
            MvcCommandGeneratorWindow.ShowWindow(isAsync: false);
        }

        [MenuItem("Assets/mvcExpress/Create/Command Async", priority = BasePriority + 1)]
        private static void OpenCommandAsyncGenerator()
        {
            MvcCommandGeneratorWindow.ShowWindow(isAsync: true);
        }
        [MenuItem("Assets/mvcExpress/Create/Proxy", priority = BasePriority + 2)]
        private static void OpenProxyGenerator()
        {
            MvcProxyGeneratorWindow.ShowWindow();
        }
        [MenuItem("Assets/mvcExpress/Create/ProxyBehaviour", priority = BasePriority + 3)]
        private static void OpenProxyBehaviourGenerator()
        {
            MvcProxyBehaviourGeneratorWindow.ShowWindow();
        }
        [MenuItem("Assets/mvcExpress/Create/MediatorBehaviour", priority = BasePriority + 4)]
        private static void OpenMediatorBehaviourGenerator()
        {
            MvcMediatorBehaviourGeneratorWindow.ShowWindow();
        }

        [MenuItem("Assets/mvcExpress/Create/View Prefab Catalog", priority = BasePriority + 11)]
        private static void CreateViewPrefabCatalog()
        {
            CreateScriptableObjectAsset<ViewPrefabCatalog>("ViewPrefabCatalog.asset");
        }



        [MenuItem("Assets/mvcExpress/Generate/Interface...", true)]
        private static bool ValidateGenerateInterface()
        {
            var obj = Selection.activeObject;
            if (obj == null) return false;
            var path = AssetDatabase.GetAssetPath(obj);
            return !string.IsNullOrEmpty(path) && path.EndsWith(".cs", System.StringComparison.OrdinalIgnoreCase);
        }

        [MenuItem("Assets/mvcExpress/Generate/Interface...")]
        private static void GenerateInterface()
        {
            var obj = Selection.activeObject;
            if (obj == null) return;

            var path = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(path)) return;

            InterfaceFromTypeGeneratorWindow.ShowForAsset(path);
        }

        [MenuItem("Assets/mvcExpress/Generate/Message...", true)]
        private static bool ValidateGenerateMessage()
        {
            var obj = Selection.activeObject;
            if (obj == null) return false;
            var path = AssetDatabase.GetAssetPath(obj);
            return !string.IsNullOrEmpty(path) && path.EndsWith(".cs", System.StringComparison.OrdinalIgnoreCase);
        }

        [MenuItem("Assets/mvcExpress/Generate/Message...")]
        private static void GenerateMessage()
        {
            var obj = Selection.activeObject;
            if (obj == null) return;

            var path = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(path)) return;

            MessageGeneratorWindow.ShowForAsset(path);
        }

        private static void CreateScriptableObjectAsset<T>(string defaultFileName) where T : ScriptableObject
        {
            var folder = GetSelectedFolderPath();
            var path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{defaultFileName}");
            var asset = ScriptableObject.CreateInstance<T>();

            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        // ========== OLD TEMPLATES (kept for reference) ==========

        private static void CreateProxyBehaviour()
        {
            CreateScript(
                "NewProxyBehaviour.cs",
                GetProxyBehaviourTemplate,
                "Create new MvcProxyBehaviour script"
            );
        }

        private static void CreateProxy()
        {
            CreateScript(
                "NewProxy.cs",
                GetProxyTemplate,
                "Create new MvcProxy (code-only) script"
            );
        }

        private static void CreateMessage()
        {
            CreateScript(
                "NewMessage.cs",
                GetMessageTemplate,
                "Create new IMessage struct"
            );
        }

        private static void CreateCommand()
        {
            CreateScript(
                "NewCommand.cs",
                GetCommandTemplate,
                "Create new MvcCommand script"
            );
        }

        private static void CreateMediator()
        {
            CreateScript(
                "NewMediator.cs",
                GetMediatorTemplate,
                "Create new MvcMediator script"
            );
        }

        private static void CreateModule()
        {
            CreateScript(
                "NewModule.cs",
                GetModuleTemplate,
                "Create new MvcModule script"
            );
        }

        private static void CreateScript(string defaultFileName, System.Func<string, string> templateFunc, string dialogTitle)
        {
            var folder = GetSelectedFolderPath();
            var path = EditorUtility.SaveFilePanelInProject(
                dialogTitle,
                defaultFileName,
                "cs",
                "Choose location for generated script.",
                folder
            );

            if (string.IsNullOrEmpty(path))
                return;

            var className = Path.GetFileNameWithoutExtension(path);
            var ns = ResolveNamespace(path);
            var contents = templateFunc(className).Replace("${NAMESPACE}", ns);

            File.WriteAllText(path, contents);
            AssetDatabase.Refresh();

            var asset = AssetDatabase.LoadAssetAtPath<Object>(path);
            if (asset != null)
            {
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
            }
        }

        private static string GetSelectedFolderPath()
        {
            var path = "Assets";
            var obj = Selection.activeObject;
            if (obj != null)
            {
                var selectedPath = AssetDatabase.GetAssetPath(obj);
                if (!string.IsNullOrEmpty(selectedPath))
                {
                    if (Directory.Exists(selectedPath))
                        path = selectedPath;
                    else
                        path = Path.GetDirectoryName(selectedPath).Replace("\\", "/");
                }
            }
            return path;
        }

        private static string ResolveNamespace(string assetPath)
        {
            // Very small heuristic: Assets/Scripts/MyGame/Foo.cs -> Game.MyGame
            assetPath = assetPath.Replace("\\", "/");
            var scriptsIndex = assetPath.IndexOf("Scripts/");
            if (scriptsIndex >= 0)
            {
                var relative = assetPath.Substring(scriptsIndex + "Scripts/".Length);
                var folder = Path.GetDirectoryName(relative)?.Replace("\\", "/");
                if (!string.IsNullOrEmpty(folder))
                {
                    folder = folder.Replace("/", ".");
                    return DefaultNamespace + "." + folder;
                }
            }
            return DefaultNamespace;
        }

        // ========== TEMPLATES ==========

        private static string GetProxyBehaviourTemplate(string className)
        {
            return "using mvcExpress;" + "\n" +
                   "using UnityEngine;" + "\n\n" +
                   "namespace ${NAMESPACE}" + "\n" +
                   "{" + "\n" +
                   "    /// <summary>" + "\n" +
                   "    /// ProxyBehaviour = model/logic that also lives on a GameObject." + "\n" +
                   "    /// - Use this when data is tightly coupled with a scene object." + "\n" +
                   "    /// - This is part of the Model in MVC. UI should not know about it directly." + "\n" +
                   "    /// </summary>" + "\n" +
                   "    public class " + className + " : MvcProxyBehaviour" + "\n" +
                   "    {" + "\n" +
                   "        // Called once when the module initializes this proxy and DI is ready." + "\n" +
                   "        // Here you usually inject other services/proxies from the module container." + "\n" +
                   "        // Example: private SomeService _service;" + "\n" +
                   "        //          protected override void OnInitialized() { _service = Inject<SomeService>(); }" + "\n" +
                   "        protected override void OnInitialized()" + "\n" +
                   "        {" + "\n" +
                   "            // TODO: Inject dependencies from Container and subscribe to messages if needed." + "\n" +
                   "            // Example (messages): Messenger.Subscribe<MyMessage>(() => { /* react to message */ });" + "\n" +
                   "        }" + "\n" +
                   "    }" + "\n" +
                   "}" + "\n";
        }

        private static string GetProxyTemplate(string className)
        {
            return "using mvcExpress;" + "\n\n" +
                   "namespace ${NAMESPACE}" + "\n" +
                   "{" + "\n" +
                   "    /// <summary>" + "\n" +
                   "    /// Code-only proxy (non-MonoBehaviour) = pure model/service." + "\n" +
                   "    /// - Use this when you want data/logic independent of any GameObject." + "\n" +
                   "    /// - Registered by a module into its Container so other actors can Inject it." + "\n" +
                   "    /// </summary>" + "\n" +
                   "    public class " + className + " : MvcProxy" + "\n" +
                   "    {" + "\n" +
                   "        // Example model data managed by this proxy." + "\n" +
                   "        // private int _score;" + "\n" +
                   "\n" +
                   "        /// <summary>" + "\n" +
                   "        /// Called once when the owning module initializes this proxy." + "\n" +
                   "        /// Inject services from the module Container and subscribe to messages here." + "\n" +
                   "        /// </summary>" + "\n" +
                   "        protected override void OnInitialized()" + "\n" +
                   "        {" + "\n" +
                   "            // Example: var config = Inject<GameConfig>();" + "\n" +
                   "            // Example: Messenger.Subscribe<SomeMessage>(() => { /* handle */ });" + "\n" +
                   "        }" + "\n" +
                   "\n" +
                   "        // Public API used by Commands/Mediators to access/modify model data." + "\n" +
                   "        // This is the only place that should directly store and mutate the data." + "\n" +
                   "        // public void AddScore(int amount) { _score += amount; Send<ScoreChangedMessage, int>(_score); }" + "\n" +
                   "    }" + "\n" +
                   "}" + "\n";
        }

        private static string GetMessageTemplate(string className)
        {
            return "using mvcExpress.Core;" + "\n\n" +
                   "namespace ${NAMESPACE}" + "\n" +
                   "{" + "\n" +
                   "    /// <summary>" + "\n" +
                   "    /// Message type for the mvcExpress Messenger." + "\n" +
                   "    /// - Implement one of the IMessage marker interfaces to define payload types." + "\n" +
                   "    /// - Commands/Proxies usually SEND messages; Mediators/Proxies usually SUBSCRIBE." + "\n" +
                   "    /// </summary>" + "\n" +
                   "    /// <remarks>" + "\n" +
                   "    /// Example usage:" + "\n" +
                   "    ///   struct PlayerDied : IMessage { }" + "\n" +
                   "    ///   struct ScoreChanged : IMessage<int> { }" + "\n" +
                   "    /// </remarks>" + "\n" +
                   "    public struct " + className + " : IMessage" + "\n" +
                   "    {" + "\n" +
                   "        // For payload, implement IMessage<T1,...> instead and add fields if needed." + "\n" +
                   "        // Example: public struct ScoreChanged : IMessage<int> { public int Value; public ScoreChanged(int value) { Value = value; } }" + "\n" +
                   "    }" + "\n" +
                   "}" + "\n";
        }

        private static string GetCommandTemplate(string className)
        {
            return "using mvcExpress;" + "\n" +
                   "using mvcExpress.Core;" + "\n\n" +
                   "namespace ${NAMESPACE}" + "\n" +
                   "{" + "\n" +
                   "    /// <summary>" + "\n" +
                   "    /// Command = small, focused piece of application logic triggered by a message or direct call." + "\n" +
                   "    /// - Use this to orchestrate model updates, call proxies, and send follow-up messages." + "\n" +
                   "    /// - Commands are part of the Controller layer in MVC." + "\n" +
                   "    /// </summary>" + "\n" +
                   "    public class " + className + " : MvcCommand" + "\n" +
                   "    {" + "\n" +
                   "        // Example injected proxy/service." + "\n" +
                   "        // private MyGameProxy _proxy;" + "\n" +
                   "\n" +
                   "        protected override void OnInitialized()" + "\n" +
                   "        {" + "\n" +
                   "            // This runs once per pooled instance. Inject proxies from the module Container here." + "\n" +
                   "            // Example: _proxy = Container.Inject<MyGameProxy>();" + "\n" +
                   "        }" + "\n" +
                   "\n" +
                   "        public override void Execute()" + "\n" +
                   "        {" + "\n" +
                   "            // TODO: Implement your command logic." + "\n" +
                   "            // Typical flow:" + "\n" +
                   "            //   1) Read/update model data via proxies." + "\n" +
                   "            //   2) Optionally send a message to notify Mediators/other actors." + "\n" +
                   "            // Example: _proxy.DoSomething();" + "\n" +
                   "            //          Messenger.Send<MyMessage>();" + "\n" +
                   "        }" + "\n" +
                   "    }" + "\n" +
                   "}" + "\n";
        }

        private static string GetMediatorTemplate(string className)
        {
            return "using UnityEngine;" + "\n" +
                   "using mvcExpress;" + "\n" +
                   "using mvcExpress.Core;" + "\n\n" +
                   "namespace ${NAMESPACE}" + "\n" +
                   "{" + "\n" +
                   "    /// <summary>" + "\n" +
                   "    /// Mediator = bridge between Unity view (MonoBehaviour) and the mvcExpress world." + "\n" +
                   "    /// - Inject proxies/services to read model state." + "\n" +
                   "    /// - Subscribe to messages to update the view when the model changes." + "\n" +
                   "    /// - Avoid heavy logic here; delegate to Commands/Proxies." + "\n" +
                   "    /// </summary>" + "\n" +
                   "    public class " + className + " : MvcMediator" + "\n" +
                   "    {" + "\n" +
                   "        // Example: cached subscription tokens if you want manual unsubscribe." + "\n" +
                   "        // private SubscriptionToken _scoreChangedToken;" + "\n" +
                   "\n" +
                   "        protected override void OnInitialized()" + "\n" +
                   "        {" + "\n" +
                   "            // Inject proxies that expose read-only data for the view." + "\n" +
                   "            // Example: var model = Container.Inject<MyGameProxy>();" + "\n" +
                   "\n" +
                   "            // Subscribe to messages that should update this view." + "\n" +
                   "            // Example:" + "\n" +
                   "            // _scoreChangedToken = Subscribe<ScoreChangedMessage, int>(OnScoreChanged);" + "\n" +
                   "        }" + "\n" +
                   "\n" +
                   "        // Example message handler called when ScoreChangedMessage is dispatched." + "\n" +
                   "        // private void OnScoreChanged(int newScore)" + "\n" +
                   "        // {" + "\n" +
                   "        //     // TODO: update UI elements here." + "\n" +
                   "        // }" + "\n" +
                   "    }" + "\n" +
                   "}" + "\n";
        }

        private static string GetModuleTemplate(string className)
        {
            return "using mvcExpress;" + "\n" +
                   "using UnityEngine;" + "\n\n" +
                   "namespace ${NAMESPACE}" + "\n" +
                   "{" + "\n" +
                   "    /// <summary>" + "\n" +
                   "    /// Module = composition root for one feature or scene." + "\n" +
                   "    /// - Registers proxies into the DI Container." + "\n" +
                   "    /// - Maps messages to Commands (in Commander)." + "\n" +
                   "    /// - Optionally attaches Mediators to scene views." + "\n" +
                   "    /// </summary>" + "\n" +
                   "    public class " + className + " : MvcModule" + "\n" +
                   "    {" + "\n" +
                   "        protected override void OnInitialized()" + "\n" +
                   "        {" + "\n" +
                   "            // 1) Register proxies into the Container so others can Inject them." + "\n" +
                   "            // Example: var proxy = new MyGameProxy();" + "\n" +
                   "            //          Container.RegisterInstance(proxy);" + "\n" +
                   "            //          proxy.Initialize(this, Container);" + "\n" +
                   "\n" +
                   "            // 2) Map messages to Commands using Commander." + "\n" +
                   "            // Example: Commander.Map<StartGameMessage, StartGameCommand>();" + "\n" +
                   "\n" +
                   "            // 3) Optionally attach Mediators to scene views via facade (if not serialized)." + "\n" +
                   "            // Example: MvcExpress.Core.MvcExpress.Instance.AttachMediator(MaduleName, someMediatorInstance);" + "\n" +
                   "        }" + "\n" +
                   "    }" + "\n" +
                   "}" + "\n";
        }
    }
}
