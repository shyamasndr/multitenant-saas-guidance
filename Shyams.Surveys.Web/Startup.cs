﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tailspin.Surveys.Data.DataModels;
using Tailspin.Surveys.Web.Security;
using Tailspin.Surveys.Security.Policy;
using Tailspin.Surveys.Web.Services;
using Constants = Tailspin.Surveys.Common.Constants;
using SurveyAppConfiguration = Tailspin.Surveys.Web.Configuration;
using Tailspin.Surveys.TokenStorage;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using System.Globalization;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Microsoft.AspNetCore.Authorization;

namespace Tailspin.Surveys.Web
{
    public class Startup
    {
        ILogger logger;
        public Startup(IHostingEnvironment env, ILogger<Startup> logger)
        {

            // Setup configuration sources.
            var builder = new ConfigurationBuilder()
                .SetBasePath(env.ContentRootPath)
                .AddJsonFile("appsettings.json")
                .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true);

            this.logger = logger;

            // Uncomment the block of code below if you want to load secrets from KeyVault
            // It is recommended to use certs for all authentication when using KeyVault
            //var config = builder.Build();
            //builder.AddAzureKeyVault(
            //    $"https://{config["KeyVault:Name"]}.vault.azure.net/",
            //    config["AzureAd:ClientId"],
            //    config["AzureAd:ClientSecret"]);

            Configuration = builder.Build();
        }

        public IConfigurationRoot Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.Configure<SurveyAppConfiguration.ConfigurationOptions>(
                options => Configuration.Bind(options)
                );

            var configOptions = new SurveyAppConfiguration.ConfigurationOptions();
            Configuration.Bind(configOptions);

            // This will add the Redis implementation of IDistributedCache
            services.AddDistributedRedisCache(setup => {
                setup.Configuration = configOptions.Redis.Configuration;
            });

            // This will only add the LocalCache implementation of IDistributedCache if there is not an IDistributedCache already registered.
            services.AddMemoryCache();

            //services.AddAuthentication(sharedOptions =>
            //    sharedOptions.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme);

            services.AddAuthorization(options =>
            {
                options.AddPolicy(PolicyNames.RequireSurveyCreator,
                    policy =>
                    {
                        policy.AddRequirements(new SurveyCreatorRequirement());
                        policy.RequireAuthenticatedUser(); // Adds DenyAnonymousAuthorizationRequirement 
                        // By adding the CookieAuthenticationDefaults.AuthenticationScheme,
                        // if an authenticated user is not in the appropriate role, they will be redirected to the "forbidden" experience.
                        policy.AddAuthenticationSchemes(CookieAuthenticationDefaults.AuthenticationScheme);
                    });

                options.AddPolicy(PolicyNames.RequireSurveyAdmin,
                    policy =>
                    {
                        policy.AddRequirements(new SurveyAdminRequirement());
                        policy.RequireAuthenticatedUser(); // Adds DenyAnonymousAuthorizationRequirement 
                        // By adding the CookieAuthenticationDefaults.AuthenticationScheme,
                        // if an authenticated user is not in the appropriate role, they will be redirected to the "forbidden" experience.
                        policy.AddAuthenticationSchemes(CookieAuthenticationDefaults.AuthenticationScheme);
                    });
            });

            // Add Entity Framework services to the services container.
            services.AddEntityFrameworkSqlServer()
                .AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(Configuration.GetSection("Data")["SurveysConnectionString"]));



            services.AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
               
            })
            .AddOpenIdConnect(x =>
            {
                x.ClientId = configOptions.AzureAd.ClientId;
                x.ClientSecret = configOptions.AzureAd.ClientSecret; // for code flow
                x.Authority = Constants.AuthEndpointPrefix;
                x.ResponseType = OpenIdConnectResponseType.CodeIdToken;
                x.SignedOutRedirectUri = configOptions.AzureAd.PostLogoutRedirectUri;
                x.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                x.TokenValidationParameters = new TokenValidationParameters { ValidateIssuer = false };
                x.Events = new SurveyAuthenticationEvents(configOptions.AzureAd,logger);
                //x.MetadataAddress = "https://login.microsoftonline.com/common/v2.0/.well-known/openid-configuration";
            })
            .AddCookie(x =>
            {
              x.Cookie.SecurePolicy = CookieSecurePolicy.Always;
              x.AccessDeniedPath = "/Home/Forbidden";
              // The default setting for cookie expiration is 14 days. SlidingExpiration is set to true by default
              x.ExpireTimeSpan = TimeSpan.FromHours(1);
              x.SlidingExpiration = true;
            });



            // Add MVC services to the services container.
            services.AddMvc();

            // Register application services.

            // This will register IDistributedCache based token cache which ADAL will use for caching access tokens.
            services.AddScoped<ITokenCacheService, DistributedTokenCacheService>();

            services.AddScoped<ISurveysTokenService, SurveysTokenService>();
            services.AddSingleton<HttpClientService>();

            // Uncomment the following line to use client certificate credentials.
            //services.AddSingleton<ICredentialService, CertificateCredentialService>();

            // Comment out the following line if you are using client certificates.
            services.AddSingleton<ICredentialService, ClientCredentialService>();

            services.AddScoped<ISurveyService, SurveyService>();
            services.AddScoped<IQuestionService, QuestionService>();
            services.AddScoped<SignInManager, SignInManager>();
            services.AddScoped<TenantManager, TenantManager>();
            services.AddScoped<UserManager, UserManager>();
            services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        }

        // Configure is called after ConfigureServices is called.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory)
        {
            var configOptions = new SurveyAppConfiguration.ConfigurationOptions();
            Configuration.Bind(configOptions);

            // Configure the HTTP request pipeline.
            // Add the following to the request pipeline only in development environment.
            if (env.IsDevelopment())
            {
                //app.UseBrowserLink();
                app.UseDeveloperExceptionPage();
                app.UseDatabaseErrorPage();
            }
            else
            {
                // Add Error handling middleware which catches all application specific errors and
                // sends the request to the following path or controller action.
                app.UseExceptionHandler("/Home/Error");
            }

            // Add static files to the request pipeline.
            app.UseStaticFiles();

            app.UseAuthentication();
            // Add MVC to the request pipeline.
            app.UseMvc(routes =>
            {
                routes.MapRoute(
                    name: "default",
                    template: "{controller=Home}/{action=Index}/{id?}");

                // Uncomment the following line to add a route for porting Web API 2 controllers.
                // routes.MapWebApiRoute("DefaultApi", "api/{controller}/{id?}");
            });
        }

      
    }
}
