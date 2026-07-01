using mvcExpress;
using mvcExpress.Samples.SingleModuleCodeApp.Controller;
using mvcExpress.Samples.SingleModuleCodeApp.Model;
using mvcExpress.Samples.SingleModuleCodeApp.Services;
using mvcExpress.Samples.SingleModuleCodeApp.View;
using UnityEngine;

namespace mvcExpress.Samples.SingleModuleCodeApp
{
    // Code-first module: this is where dependencies, command bindings, and mediators are wired.
    public sealed class SingleModuleCodeAppModule : MvcModule
    {
        [SerializeField] private int _startScore;
        [SerializeField] private string _scoreLabel = "Score";

        protected override void RegisterServices()
        {
            // Register plain C# services from code instead of using the Services registry.
            Container.Register(new CodeScoreFormatterService(_scoreLabel))
                .ToView()
                .AsPersistent();
        }

        protected override void RegisterProxies()
        {
            // Commands can mutate the concrete proxy; mediators only get the read-only interface.
            Container.Register(new CodeScoreProxy(_startScore))
                .ToLogic()
                .ToViewAs<ICodeScoreReadModel>()
                .AsPersistent();
        }

        protected override void BindCommands()
        {
            // These logs should say "by code" because the bindings are authored here.
            Commander.Bind<CodeAddScoreCommand, CodeAddScoreClickedMessage, int>();
            Commander.Bind<CodeResetScoreCommand, CodeResetScoreClickedMessage>();
        }

        protected override void AttachMediators()
        {
            // The concrete Canvas hierarchy lives in a prefab; code decides when it enters this module.
            MediatorHub.AttachPrefab<SingleModuleCodeCanvasMediatorBehaviour>(gameObject.scene);
        }
    }
}
