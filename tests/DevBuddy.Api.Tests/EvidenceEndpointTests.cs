using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace DevBuddy.Api.Tests;

/// <summary>
/// The upload and download routes over real HTTP.
/// <para>
/// These two do not go through the operation manifest, because bytes do not belong in a JSON
/// envelope — so they are the one pair of operations whose transport is not covered by the
/// generated client or the dispatcher tests. If the multipart binding breaks, nothing else in the
/// suite notices.
/// </para>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class EvidenceEndpointTests(ApiFixture fixture)
{
    [Fact]
    public async Task an_uploaded_file_comes_back_byte_for_byte()
    {
        using HttpClient client = await fixture.SignInAsAdministratorAsync();

        byte[] content = "a build log from the API test, nothing sensitive"u8.ToArray();

        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(file, "file", "build.log");
        form.Add(new StringContent("The build log for this test"), "description");

        HttpResponseMessage upload = await client.PostAsync(EvidenceRoute(), form);

        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);

        JsonElement created = await upload.Content.ReadFromJsonAsync<JsonElement>();
        string evidenceId = created.GetProperty("evidenceId").GetString()!;

        Assert.Equal("Clean", created.GetProperty("redactionState").GetString());

        // Listing it is a separate operation with its own audit entry, and it goes through the
        // ordinary dispatch route because metadata is ordinary JSON.
        JsonElement listed = await InvokeAsync(client, "list_evidence");

        Assert.Contains(
            listed.GetProperty("evidence").EnumerateArray(),
            item => item.GetProperty("evidenceId").GetString() == evidenceId);

        HttpResponseMessage download = await client.GetAsync(
            $"{EvidenceRoute()}/{evidenceId}");

        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(content, await download.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task a_file_carrying_a_credential_is_refused_with_422()
    {
        using HttpClient client = await fixture.SignInAsAdministratorAsync();

        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent("AWS_ACCESS_KEY_ID=AKIAIOSFODNN7EXAMPLE"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(file, "file", "leaky.log");
        form.Add(new StringContent("A log nobody read first"), "description");

        HttpResponseMessage upload = await client.PostAsync(EvidenceRoute(), form);

        // The same 422 a draft carrying a credential gets. The scanner ran on the way in, before
        // anything was written, so this is a refusal rather than a cleanup.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, upload.StatusCode);

        string body = await upload.Content.ReadAsStringAsync();
        Assert.Contains("aws-access-key-id", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task uploading_without_a_token_is_refused()
    {
        using HttpClient client = fixture.Factory.CreateClient();

        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent("x"u8.ToArray()), "file", "x.txt");
        form.Add(new StringContent("no token"), "description");

        HttpResponseMessage upload = await client.PostAsync(EvidenceRoute(), form);

        Assert.Equal(HttpStatusCode.Unauthorized, upload.StatusCode);
    }

    private string EvidenceRoute() =>
        $"/workspaces/{fixture.Workspace.Value}/projects/{fixture.Project.Value}/evidence";

    private async Task<JsonElement> InvokeAsync(HttpClient client, string operation)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/operations/{operation}",
            new
            {
                scope = new
                {
                    workspaceId = fixture.Workspace.Value,
                    projectId = fixture.Project.Value,
                },
            });

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
