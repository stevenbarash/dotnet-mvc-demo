using System.Net;
using System.Text.Json;
using DescopeDemo.Web.Models;
using DescopeDemo.Web.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace DescopeDemo.Tests.Services;

public sealed class DescopeAppServiceTests
{
    private static IConfiguration BuildConfig(string projectId = "proj123", string managementKey = "mgmtkey")
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Descope:ProjectId"] = projectId,
                ["Descope:ManagementKey"] = managementKey
            })
            .Build();
    }

    private static HttpClient BuildMockHttpClient(Dictionary<string, HttpResponseMessage> responses)
    {
        var handler = new FakeHttpMessageHandler(responses);
        return new HttpClient(handler) { BaseAddress = new Uri("https://api.descope.com") };
    }

    [Fact]
    public async Task GetTenantAppsAsync_ReturnsMappedApps_WhenApiRespondsSuccessfully()
    {
        var ssoSettingsResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                federatedAppIds = new[] { "app1", "app2" }
            }))
        };

        var app1Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                id = "app1",
                name = "Digital Mail",
                logo = "https://example.com/logo.png",
                enabled = true,
                appType = "saml",
                samlSettings = new { idpInitiatedUrl = "https://api.descope.com/saml/idp/init/app1" }
            }))
        };

        var app2Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                id = "app2",
                name = "Reporting",
                logo = (string?)null,
                enabled = true,
                appType = "oidc",
                oidcSettings = new { customIdpInitiatedLoginPageUrl = "https://example.com/oidc-init" }
            }))
        };

        var responses = new Dictionary<string, HttpResponseMessage>
        {
            ["/v1/mgmt/tenant?id=tenant1"] = ssoSettingsResponse,
            ["/v1/mgmt/sso/idp/app/load?id=app1"] = app1Response,
            ["/v1/mgmt/sso/idp/app/load?id=app2"] = app2Response
        };

        var httpClient = BuildMockHttpClient(responses);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("DescopeManagement")).Returns(httpClient);

        var logger = new Mock<ILogger<DescopeAppService>>();
        var config = BuildConfig();

        var service = new DescopeAppService(factory.Object, config, logger.Object);
        var result = await service.GetTenantAppsAsync("tenant1");

        Assert.Equal(2, result.Count);

        Assert.Equal("app1", result[0].Id);
        Assert.Equal("Digital Mail", result[0].Name);
        Assert.Equal("https://example.com/logo.png", result[0].Logo);
        Assert.Equal("https://api.descope.com/saml/idp/init/app1", result[0].IdpInitiatedUrl);
        Assert.True(result[0].Enabled);

        Assert.Equal("app2", result[1].Id);
        Assert.Equal("Reporting", result[1].Name);
        Assert.Null(result[1].Logo);
        Assert.Equal("https://example.com/oidc-init", result[1].IdpInitiatedUrl);
    }

    [Fact]
    public async Task GetTenantAppsAsync_FiltersDisabledApps()
    {
        var ssoResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                federatedAppIds = new[] { "app1" }
            }))
        };

        var appResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                id = "app1",
                name = "Disabled App",
                logo = (string?)null,
                enabled = false,
                appType = "saml",
                samlSettings = new { idpInitiatedUrl = "https://example.com/init" }
            }))
        };

        var responses = new Dictionary<string, HttpResponseMessage>
        {
            ["/v1/mgmt/tenant?id=tenant1"] = ssoResponse,
            ["/v1/mgmt/sso/idp/app/load?id=app1"] = appResponse
        };

        var httpClient = BuildMockHttpClient(responses);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("DescopeManagement")).Returns(httpClient);

        var logger = new Mock<ILogger<DescopeAppService>>();
        var config = BuildConfig();

        var service = new DescopeAppService(factory.Object, config, logger.Object);
        var result = await service.GetTenantAppsAsync("tenant1");

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetTenantAppsAsync_ReturnsEmpty_WhenNoFederatedApps()
    {
        var ssoResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                federatedAppIds = Array.Empty<string>()
            }))
        };

        var responses = new Dictionary<string, HttpResponseMessage>
        {
            ["/v1/mgmt/tenant?id=tenant1"] = ssoResponse
        };

        var httpClient = BuildMockHttpClient(responses);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("DescopeManagement")).Returns(httpClient);

        var logger = new Mock<ILogger<DescopeAppService>>();
        var config = BuildConfig();

        var service = new DescopeAppService(factory.Object, config, logger.Object);
        var result = await service.GetTenantAppsAsync("tenant1");

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetTenantAppsAsync_ConstructsSamlFallbackUrl_WhenIdpInitiatedUrlMissing()
    {
        var tenantResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                federatedAppIds = new[] { "app1" }
            }))
        };

        var appResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                id = "app1",
                name = "SAML App No URL",
                logo = (string?)null,
                enabled = true,
                appType = "saml",
                samlSettings = new { }
            }))
        };

        var responses = new Dictionary<string, HttpResponseMessage>
        {
            ["/v1/mgmt/tenant?id=tenant1"] = tenantResponse,
            ["/v1/mgmt/sso/idp/app/load?id=app1"] = appResponse
        };

        var httpClient = BuildMockHttpClient(responses);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("DescopeManagement")).Returns(httpClient);

        var logger = new Mock<ILogger<DescopeAppService>>();
        var config = BuildConfig();

        var service = new DescopeAppService(factory.Object, config, logger.Object);
        var result = await service.GetTenantAppsAsync("tenant1");

        Assert.Single(result);
        Assert.Equal("https://api.descope.com/v1/auth/saml/idp/initiate?app=proj123-app1", result[0].IdpInitiatedUrl);
    }

    [Fact]
    public async Task GetTenantAppsAsync_ReturnsSuccessfulApps_WhenOneAppFails()
    {
        var tenantResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                federatedAppIds = new[] { "app1", "app2" }
            }))
        };

        var app1Response = new HttpResponseMessage(HttpStatusCode.InternalServerError);

        var app2Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                id = "app2",
                name = "Working App",
                logo = (string?)null,
                enabled = true,
                appType = "saml",
                samlSettings = new { idpInitiatedUrl = "https://example.com/init" }
            }))
        };

        var responses = new Dictionary<string, HttpResponseMessage>
        {
            ["/v1/mgmt/tenant?id=tenant1"] = tenantResponse,
            ["/v1/mgmt/sso/idp/app/load?id=app1"] = app1Response,
            ["/v1/mgmt/sso/idp/app/load?id=app2"] = app2Response
        };

        var httpClient = BuildMockHttpClient(responses);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("DescopeManagement")).Returns(httpClient);

        var logger = new Mock<ILogger<DescopeAppService>>();
        var config = BuildConfig();

        var service = new DescopeAppService(factory.Object, config, logger.Object);
        var result = await service.GetTenantAppsAsync("tenant1");

        Assert.Single(result);
        Assert.Equal("app2", result[0].Id);
        Assert.Equal("Working App", result[0].Name);
    }

    [Fact]
    public async Task GetTenantAppsAsync_ReturnsEmpty_WhenSsoSettingsFails()
    {
        var responses = new Dictionary<string, HttpResponseMessage>
        {
            ["/v1/mgmt/tenant?id=tenant1"] = new HttpResponseMessage(HttpStatusCode.InternalServerError)
        };

        var httpClient = BuildMockHttpClient(responses);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("DescopeManagement")).Returns(httpClient);

        var logger = new Mock<ILogger<DescopeAppService>>();
        var config = BuildConfig();

        var service = new DescopeAppService(factory.Object, config, logger.Object);
        var result = await service.GetTenantAppsAsync("tenant1");

        Assert.Empty(result);
    }
}

public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Dictionary<string, HttpResponseMessage> _responses;

    public FakeHttpMessageHandler(Dictionary<string, HttpResponseMessage> responses)
    {
        _responses = responses;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var key = request.RequestUri?.PathAndQuery ?? "";
        if (_responses.TryGetValue(key, out var response))
            return Task.FromResult(response);

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
