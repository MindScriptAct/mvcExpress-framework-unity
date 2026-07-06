namespace mvcExpress.Samples.SingleModuleCodeApp.Services
{
    // Plain C# service registered from module code.
    public sealed class CodeScoreFormatterService
    {
        private readonly string _prefix;

        public CodeScoreFormatterService(string label)
        {
            _prefix = string.IsNullOrWhiteSpace(label) ? "Score" : label.Trim();
        }

        public string Format(int score)
        {
            return $"{_prefix}: {score}";
        }
    }
}
