using Microsoft.AspNetCore.Mvc;
using Sweeprr.API.Controllers;
using Xunit;

namespace Sweeprr.Tests.Controllers;

public class SystemControllerTests
{
    [Fact]
    public void Info_Returns_OkResult_With_Correct_Version()
    {
        // Arrange
        var controller = new SystemController();

        // Act
        var result = controller.Info();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var val = okResult.Value;
        Assert.NotNull(val);

        var versionProp = val.GetType().GetProperty("version");
        Assert.NotNull(versionProp);
        var versionValue = versionProp.GetValue(val) as string;
        Assert.Equal("1.1.0", versionValue);

        var releaseDateProp = val.GetType().GetProperty("releaseDate");
        Assert.NotNull(releaseDateProp);
        var releaseDateValue = releaseDateProp.GetValue(val) as string;
        Assert.NotNull(releaseDateValue);
    }
}
