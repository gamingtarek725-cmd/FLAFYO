using Flafyo.Core;
using Xunit;

namespace Flafyo.Core.Tests;

public class AiApiTranslatorTests
{
    [Fact]
    public void Builds_an_openai_style_request_with_the_system_prompt_and_the_text()
    {
        var json = AiApiTranslator.BuildOpenAiRequest("gpt-4o-mini", "Find the key");
        Assert.Contains("\"model\":\"gpt-4o-mini\"", json);
        Assert.Contains("Find the key", json);
        Assert.Contains("translate video game text", json);
    }

    [Fact]
    public void Parses_the_openai_response_shape() =>
        Assert.Equal("جد المفتاح", AiApiTranslator.ParseOpenAiResponse(
            "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"جد المفتاح\"}}]}"));

    [Fact]
    public void Builds_an_anthropic_request_with_system_and_user_message()
    {
        var json = AiApiTranslator.BuildAnthropicRequest("claude-3-5-sonnet-latest", "Open the door");
        Assert.Contains("\"model\":\"claude-3-5-sonnet-latest\"", json);
        Assert.Contains("Open the door", json);
    }

    [Fact]
    public void Parses_the_anthropic_response_shape() =>
        Assert.Equal("افتح الباب", AiApiTranslator.ParseAnthropicResponse(
            "{\"content\":[{\"type\":\"text\",\"text\":\"افتح الباب\"}]}"));
}
