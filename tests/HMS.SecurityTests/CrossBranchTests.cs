using System.Net;
using System.Net.Http.Json;
using System.Text;
using HMS.Api.IntegrationTests;
using HMS.Application.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HMS.SecurityTests;

/// <summary>
/// A manager of one hotel tries every branch endpoint against another hotel, and tries to reach the other hotel's
/// records through their own branch's routes. Nothing may succeed, and nothing may crash.
/// </summary>
public sealed class CrossBranchTests(HmsApiFactory factory) : IClassFixture<HmsApiFactory>
{
    private static readonly HashSet<string> Writes = ["POST", "PUT", "PATCH", "DELETE"];

    [Fact]
    public async Task A_manager_of_another_branch_is_refused_on_every_branch_endpoint()
    {
        var target = await HotelSetup.CreateAsync(factory, roomCount: 1);
        var other = await HotelSetup.CreateAsync(factory, roomCount: 1);
        var outsider = await other.StaffAsync(Roles.HotelManager);

        _ = factory.Server;
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.Parameters.Any(p => p.Name == "branchId"))
            .ToList();
        Assert.True(endpoints.Count > 50, $"Only {endpoints.Count} branch endpoints found.");

        var problems = new List<string>();
        foreach (var endpoint in endpoints)
        {
            foreach (var method in endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods)
            {
                var url = "/" + Fill(endpoint, target.Branch.Id);
                using var request = new HttpRequestMessage(new HttpMethod(method), url);
                if (Writes.Contains(method))
                {
                    request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
                    request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
                }
                using var response = await outsider.SendAsync(request);
                var status = (int)response.StatusCode;
                // 403 is the expected answer. Writes may stop earlier at request validation (400) or the upload
                // size/type checks (415), which still reveals nothing about the other hotel.
                var acceptable = status == 403 || (Writes.Contains(method) && status is 400 or 415);
                if (!acceptable) problems.Add($"{method} {url} returned {status}");
            }
        }
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public async Task Records_of_another_branch_cannot_be_reached_through_your_own_branch()
    {
        var target = await HotelSetup.CreateAsync(factory, roomCount: 2);
        var other = await HotelSetup.CreateAsync(factory, roomCount: 1);
        var outsider = await other.StaffAsync(Roles.HotelManager);
        var booking = await target.BookAsync(nights: 2);
        Assert.NotNull(booking.FolioId);

        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync(other.P($"reservations/{booking.Id}"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync(other.P($"folios/{booking.FolioId}"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync(other.P($"guests/{booking.Guest.Id}"))).StatusCode);

        var cancel = await outsider.PostAsJsonAsync(other.P($"reservations/{booking.Id}/cancel"), new { reason = "not mine" });
        Assert.True(cancel.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest, $"cancel returned {(int)cancel.StatusCode}");
        var charge = await HotelSetup.PostKeyedAsync(outsider, other.P($"folios/{booking.FolioId}/charges"),
            new { serviceItemId = other.Laundry.Id, quantity = 1 });
        Assert.True(charge.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest, $"charge returned {(int)charge.StatusCode}");

        // The record is untouched.
        var after = await target.GetAsync<HMS.Contracts.Reservations.ReservationDto>($"reservations/{booking.Id}");
        Assert.Equal(booking.Status, after.Status);
        var folio = await target.FolioAsync(booking.FolioId!.Value);
        Assert.Equal(0, folio.Charges.Count(c => c.Description.Contains("Laundry", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task Branch_lists_only_show_your_own_branches()
    {
        var target = await HotelSetup.CreateAsync(factory, roomCount: 1);
        var other = await HotelSetup.CreateAsync(factory, roomCount: 1);
        var outsider = await other.StaffAsync(Roles.Receptionist);
        var body = await outsider.GetStringAsync("/api/v1/branches");
        Assert.Contains(other.Branch.Id.ToString(), body);
        Assert.DoesNotContain(target.Branch.Id.ToString(), body);
    }

    private static string Fill(RouteEndpoint endpoint, Guid branchId)
    {
        var text = endpoint.RoutePattern.RawText!.TrimStart('/');
        foreach (var parameter in endpoint.RoutePattern.Parameters)
        {
            var value = parameter.Name == "branchId" ? branchId.ToString()
                : parameter.ParameterPolicies.Any(p => p.Content == "guid") ? Guid.NewGuid().ToString()
                : parameter.ParameterPolicies.Any(p => p.Content is "int" or "long") ? "1"
                : parameter.ParameterPolicies.Any(p => p.Content == "datetime") ? "2026-01-01"
                : "X";
            var start = text.IndexOf("{" + parameter.Name, StringComparison.Ordinal);
            var end = text.IndexOf('}', start);
            text = text[..start] + value + text[(end + 1)..];
        }
        return text;
    }
}
