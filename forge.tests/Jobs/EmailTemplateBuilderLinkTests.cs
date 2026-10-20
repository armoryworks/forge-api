using FluentAssertions;

using Forge.Api.Jobs;

namespace Forge.Tests.Jobs;

public class EmailTemplateBuilderLinkTests
{
    [Fact]
    public void Digest_links_preferences_to_the_install_and_has_no_dead_links()
    {
        var html = EmailTemplateBuilder.BuildDigest("Acme", "Sam", [], [], 1, appBaseUrl: "https://forge.example.test/");

        html.Should().NotContain("href=\"#\"");
        html.Should().Contain("href=\"https://forge.example.test/notifications?tab=preferences\"");
    }

    [Fact]
    public void Digest_without_a_base_url_omits_the_preferences_link()
    {
        var html = EmailTemplateBuilder.BuildDigest("Acme", "Sam", [], [], 1);

        html.Should().NotContain("href=\"#\"");
        html.Should().NotContain("Manage preferences");
    }

    [Fact]
    public void Notification_button_opens_the_notifications_page()
    {
        var html = EmailTemplateBuilder.BuildNotificationEmail("Acme", "Sam", "Heads up", "Line one\nLine two", appBaseUrl: "https://forge.example.test");

        html.Should().Contain("href=\"https://forge.example.test/notifications\"");
        html.Should().Contain("Line one<br>Line two");
        html.Should().NotContain("href=\"#\"");
    }

    [Fact]
    public void Notification_with_an_action_uses_it_and_still_encodes_the_message()
    {
        var html = EmailTemplateBuilder.BuildNotificationEmail(
            "Acme", "Sam", "Welcome", "Use <b>this</b> link", appBaseUrl: "https://forge.example.test",
            actionUrl: "https://forge.example.test/setup/abc", actionLabel: "Complete Setup");

        html.Should().Contain("href=\"https://forge.example.test/setup/abc\"");
        html.Should().Contain("Complete Setup");
        html.Should().Contain("Use &lt;b&gt;this&lt;/b&gt; link");
    }

    [Fact]
    public void Notification_without_any_url_has_no_button()
    {
        var html = EmailTemplateBuilder.BuildNotificationEmail("Acme", "Sam", "Heads up", "Hello");

        html.Should().NotContain("<a ");
    }
}
