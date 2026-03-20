using Domain.Interfaces;
using Infrastructure.Identity;
using Infrastructure.MultiTenant;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Application.Interfaces;
using Microsoft.AspNetCore.Identity;
using Infrastructure.Services;

namespace Infrastructure
{
    public static class InfrastructureServiceExtensions
    {
        public static IServiceCollection AddInfrastructure(
            this IServiceCollection services,
            IConfiguration config)
        {
            // Cache
            services.AddMemoryCache();

            // Identity UserManager con DbContext del tenant
            services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.Password.RequireDigit = true;
                options.Password.RequiredLength = 8;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireUppercase = true;
                options.User.RequireUniqueEmail = true;
            })
            .AddRoles<IdentityRole<int>>()
            .AddEntityFrameworkStores<BaseAppDbContext>()
            .AddDefaultTokenProviders();

            // Sostituisci il DbContext usato da Identity con quello del tenant
            services.AddScoped<BaseAppDbContext>(sp =>
            {
                var factory = sp.GetRequiredService<ITenantDbContextFactory>();
                return (BaseAppDbContext)factory.Create();
            });

            // AuthService
            services.AddScoped<IAuthService, AuthService>();
            // Social Layer services
            services.AddScoped<INotificationService, NotificationService>();
            services.AddScoped<IActivityService, ActivityService>();
            services.AddScoped<IChatService, ChatService>();
            // Provisioning
            services.AddScoped<ITenantProvisioningService, TenantProvisioningService>();
            // Migration background service
            services.AddHostedService<TenantMigrationHostedService>();

            // MultiTenant
            services.AddScoped<TenantContext>();
            services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
            services.AddScoped<ITenantDbContextFactory, TenantDbContextFactory>();
            services.AddScoped<ITenantResolver, TenantResolver>();
            services.AddSingleton<ITenantEncryption, TenantEncryption>();

            // Identity
            services.AddScoped<IJwtService, JwtService>();

            // JWT Authentication
            var jwtKey = config["Jwt:Key"]
                ?? throw new InvalidOperationException("Jwt:Key non configurata.");

            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = config["Jwt:Issuer"],
                    ValidAudience = config["Jwt:Audience"],
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(jwtKey))
                };
            });

            services.AddAuthorization(options =>
            {
                options.AddPolicy("InternalOnly", policy =>
                    policy.RequireClaim("userType", "Internal"));
                options.AddPolicy("ExternalOnly", policy =>
                    policy.RequireClaim("userType", "External"));
            });

            return services;
        }

        public static IApplicationBuilder UseMultiTenant(this IApplicationBuilder app)
        {
            app.UseMiddleware<TenantResolutionMiddleware>();
            return app;
        }
    }
}
