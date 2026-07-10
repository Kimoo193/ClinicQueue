using ClinicQueue.Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;

namespace ClinicQueue.Api.Authorization;

public static class AuthorizationExtensions
{
    public static IServiceCollection AddClinicQueueAuthorization(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.Patient, p => p.RequireRole(Roles.Patient, Roles.ClinicOwner, Roles.SuperAdmin))
            .AddPolicy(Policies.Receptionist, p => p.RequireRole(Roles.Receptionist, Roles.ClinicOwner, Roles.SuperAdmin))
            .AddPolicy(Policies.Doctor, p => p.RequireRole(Roles.Doctor, Roles.ClinicOwner, Roles.SuperAdmin))
            .AddPolicy(Policies.ClinicOwner, p => p.RequireRole(Roles.ClinicOwner, Roles.SuperAdmin))
            .AddPolicy(Policies.SuperAdmin, p => p.RequireRole(Roles.SuperAdmin))
            .AddPolicy(Policies.ClinicStaff, p => p.RequireRole(
                Roles.Receptionist, Roles.Doctor, Roles.ClinicOwner, Roles.SuperAdmin))
            .AddPolicy(Policies.PatientOrReceptionist, p => p.RequireRole(
                Roles.Patient, Roles.Receptionist, Roles.ClinicOwner, Roles.SuperAdmin));

        return services;
    }
}
