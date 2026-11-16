using KeHoachSX.Infrastructure.Database;
using KeHoachSX.Infrastructure.Repositories;
using Microsoft.Extensions.DependencyInjection;
namespace KeHoachSX.Infrastructure;
public static class KeHoachSXInfrastructureExtensions
{
    public static IServiceCollection AddKeHoachSXInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<ISqlConnectionFactory, SqlConnectionFactory>();
        services.AddScoped<IAssemblyEmployeeRepository, AssemblyEmployeeRepository>();
        services.AddScoped<IStandardTimeRepository, StandardTimeRepository>();
        return services;
    }
}
