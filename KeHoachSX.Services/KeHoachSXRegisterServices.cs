using Interfaces;
using JtecShare.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Services;

namespace KeHoachSX.Infrastructure;

public sealed class KeHoachSXRegisterServices : IModuleRegistration
{
    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddKeHoachSXInfrastructure();

        services.AddSingleton<ISkillMatrixService, SkillMatrixService>();
        services.AddSingleton<IStandardTimeService, StandardTimeService>();

        services.AddScoped<IAssemblyEmployeeService, AssemblyEmployeeService>();
        services.AddScoped<IWeeklyPlanParserService, WeeklyPlanParserService>();
        services.AddScoped<IAutoDispatchService, AutoDispatchService>();
    }

    public void Initialize(IServiceProvider serviceProvider)
    {
        // Skill matrix is loaded lazily on first use to avoid fire-and-forget startup faults.
    }
}
