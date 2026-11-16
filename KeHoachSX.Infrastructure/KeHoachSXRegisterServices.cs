using Interfaces;
using JtecShare.Core.Interfaces;
using KeHoachSX.Infrastructure.Database;
using KeHoachSX.Infrastructure.DataSources;
using KeHoachSX.Infrastructure.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Services;
using Abstractions;

namespace KeHoachSX.Infrastructure;

public sealed class KeHoachSXRegisterServices : IModuleRegistration
{
    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        // Database and file data adapters.
        services.AddSingleton<ISqlConnectionFactory, SqlConnectionFactory>();
        services.AddScoped<IAssemblyEmployeeRepository, SqlAssemblyEmployeeRepository>();
        services.AddScoped<IStandardTimeRepository, SqlStandardTimeRepository>();
        services.AddSingleton<IAssemblyEmployeeJsonSource, JsonAssemblyEmployeeSource>();
        services.AddSingleton<ISkillMatrixDataSource, CsvSkillMatrixDataSource>();
        services.AddScoped<IWeeklyPlanFileReader, WeeklyPlanFileReader>();

        // Oracle remains optional, as it was in the previous implementation.
        services.AddScoped<IOracleStandardTimeProvider>(provider =>
        {
            var oracle = provider.GetService<ILayGioCongHeThong>();
            return oracle is null
                ? new NullOracleStandardTimeProvider()
                : new OracleStandardTimeProvider(oracle);
        });

        // Application services contain no SQL/JSON/CSV/Oracle provider implementation.
        services.AddSingleton<ISkillMatrixService, SkillMatrixService>();
        services.AddScoped<IAssemblyEmployeeService, AssemblyEmployeeService>();
        services.AddScoped<IStandardTimeService, StandardTimeService>();
        services.AddScoped<IWeeklyPlanParserService, WeeklyPlanParserService>();
        services.AddScoped<IAutoDispatchService, AutoDispatchService>();
    }

    public void Initialize(IServiceProvider serviceProvider)
    {
        // Load the skill matrix on demand instead of fire-and-forget startup work.
    }
}
