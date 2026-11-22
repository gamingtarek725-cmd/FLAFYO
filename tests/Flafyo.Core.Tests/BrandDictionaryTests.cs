using Flafyo.Core;
using Xunit;

namespace Flafyo.Core.Tests;

public class BrandDictionaryTests
{
    [Theory]
    [InlineData("iRacing", "آي ريسنج")]
    [InlineData("IRACING ARCADE", "آي ريسنج آركيد")]
    [InlineData("NVIDIA", "إنفيديا")]
    [InlineData("PlayStation", "بلايستيشن")]
    public void Brand_names_have_arabic_spellings(string source, string expected)
    {
        var brands = BrandDictionary.Load();
        Assert.True(brands.TryGetValue(source, out var actual));
        Assert.Equal(expected, actual);
    }
}
