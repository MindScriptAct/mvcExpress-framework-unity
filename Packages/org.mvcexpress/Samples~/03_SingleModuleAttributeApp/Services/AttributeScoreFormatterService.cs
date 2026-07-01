using mvcExpress;

namespace mvcExpress.Samples.SingleModuleAttributeApp.Services
{
    // Plain C# service discovered and registered through [Register].
    [Register(
        typeof(SingleModuleAttributeAppModule),
        RegisterToLogic = false,
        RegisterToView = true)]
    public sealed class AttributeScoreFormatterService
    {
        private const string Prefix = "Score";

        public string Format(int score)
        {
            return $"{Prefix}: {score}";
        }
    }
}
