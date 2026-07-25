using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Netdocs.Core.Tests;

public class TemplateBlockValidatorTests
{
    private sealed class TestLogger : ILogger
    {
        public List<string> Warnings { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
                Warnings.Add(formatter(state, exception));
        }
    }

    [Fact]
    public void DetectsNoDuplicates_EmitsNoWarning()
    {
        var logger = new TestLogger();
        var validator = new Netdocs.Core.Templating.TemplateBlockValidator(logger);
        var content = "{% block header %}...{% endblock %}\n{% block content %}...{% endblock %}";

        validator.Validate("test.html", content);

        Assert.Empty(logger.Warnings);
    }

    [Fact]
    public void DetectsDuplicateBlocks_EmitsWarning()
    {
        var logger = new TestLogger();
        var validator = new Netdocs.Core.Templating.TemplateBlockValidator(logger);
        var content = "{% block header %}first{% endblock %}\n{% block header %}second{% endblock %}";

        validator.Validate("test.html", content);

        Assert.Single(logger.Warnings);
        Assert.Contains("header", logger.Warnings[0]);
        Assert.Contains("2 definitions", logger.Warnings[0]);
    }

    [Fact]
    public void DetectsMultipleDuplicates_EmitsWarningPerBlock()
    {
        var logger = new TestLogger();
        var validator = new Netdocs.Core.Templating.TemplateBlockValidator(logger);
        var content = @"
{% block header %}a{% endblock %}
{% block header %}b{% endblock %}
{% block footer %}x{% endblock %}
{% block footer %}y{% endblock %}
{% block footer %}z{% endblock %}
";

        validator.Validate("test.html", content);

        Assert.Equal(2, logger.Warnings.Count);
        Assert.Contains("header", logger.Warnings[0]);
        Assert.Contains("footer", logger.Warnings[1]);
    }

    [Fact]
    public void CaseInsensitive_TreatsHeaderAndHEADERAsIdentical()
    {
        var logger = new TestLogger();
        var validator = new Netdocs.Core.Templating.TemplateBlockValidator(logger);
        var content = "{% block header %}{% endblock %}\n{% block HEADER %}{% endblock %}";

        validator.Validate("test.html", content);

        Assert.Single(logger.Warnings);
    }
}
