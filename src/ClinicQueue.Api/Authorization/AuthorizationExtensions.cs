using ClinicQueue.Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;

namespace ClinicQueue.Api.Authorization;

public static class AuthorizationExtensions
{
    public static IServiceCollection AddClinicQueueAuthorization(this IServiceCollection services)
    {
        // Every clinic-scoped policy also requires that the token's tenant context is still
        // backed by an active membership at request time (SuperAdmin bypasses in the handler).
        services.AddScoped<IAuthorizationHandler, ActiveMembershipHandler>();

        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.Patient, p => p
                .RequireRole(Roles.Patient, Roles.ClinicOwner, Roles.SuperAdmin)
                .AddRequirements(new ActiveMembershipRequirement()))
            .AddPolicy(Policies.Receptionist, p => p
                .RequireRole(Roles.Receptionist, Roles.ClinicOwner, Roles.SuperAdmin)
                .AddRequirements(new ActiveMembershipRequirement()))
            .AddPolicy(Policies.Doctor, p => p
                .RequireRole(Roles.Doctor, Roles.ClinicOwner, Roles.SuperAdmin)
                .AddRequirements(new ActiveMembershipRequirement()))
            .AddPolicy(Policies.ClinicOwner, p => p
                .RequireRole(Roles.ClinicOwner, Roles.SuperAdmin)
                .AddRequirements(new ActiveMembershipRequirement()))
            .AddPolicy(Policies.SuperAdmin, p => p.RequireRole(Roles.SuperAdmin))
            .AddPolicy(Policies.ClinicStaff, p => p
                .RequireRole(Roles.Receptionist, Roles.Doctor, Roles.ClinicOwner, Roles.SuperAdmin)
                .AddRequirements(new ActiveMembershipRequirement()))
            .AddPolicy(Policies.PatientOrReceptionist, p => p
                .RequireRole(Roles.Patient, Roles.Receptionist, Roles.ClinicOwner, Roles.SuperAdmin)
                .AddRequirements(new ActiveMembershipRequirement()))
            .AddPolicy(Policies.PatientOrDoctor, p => p
                .RequireRole(Roles.Patient, Roles.Doctor, Roles.ClinicOwner, Roles.SuperAdmin)
                .AddRequirements(new ActiveMembershipRequirement()));

        return services;
    }
}
