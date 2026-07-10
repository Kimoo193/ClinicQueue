using ClinicQueue.Application.Queue.Booking;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ClinicQueue.Api.Endpoints.Queue;

public static class ClinicSearchEndpoints
{
    public static RouteGroupBuilder MapClinicSearchEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/clinics/search", SearchClinicsAsync)
            .AllowAnonymous()
            .WithName("SearchClinics");

        return group;
    }

    private static async Task<IResult> SearchClinicsAsync(
        string? specialty,
        string? name,
        string? location,
        IClinicSearchHandler handler,
        IValidator<ClinicSearchRequest> validator,
        CancellationToken cancellationToken)
    {
        var request = new ClinicSearchRequest(specialty, name, location);
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Results.ValidationProblem(validation.ToDictionary());
        }

        var results = await handler.HandleAsync(
            new ClinicSearchQuery(specialty, name, location),
            cancellationToken);

        return Results.Ok(results.Select(r => new
        {
            clinicId = r.ClinicId,
            name = r.Name,
            specialty = r.Specialty,
            aggregateRating = r.AggregateRating,
            location = r.Location
        }));
    }
}
