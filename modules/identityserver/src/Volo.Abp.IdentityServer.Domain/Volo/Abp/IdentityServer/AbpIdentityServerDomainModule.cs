﻿using System.Collections;
using System.Collections.Generic;
using IdentityServer4.Services;
using IdentityServer4.Stores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Volo.Abp.AutoMapper;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Caching;
using Volo.Abp.Domain.Entities.Events.Distributed;
using Volo.Abp.Identity;
using Volo.Abp.IdentityServer.ApiResources;
using Volo.Abp.IdentityServer.AspNetIdentity;
using Volo.Abp.IdentityServer.ApiScopes;
using Volo.Abp.IdentityServer.Clients;
using Volo.Abp.IdentityServer.Devices;
using Volo.Abp.IdentityServer.IdentityResources;
using Volo.Abp.IdentityServer.Tokens;
using Volo.Abp.Modularity;
using Volo.Abp.ObjectExtending;
using Volo.Abp.ObjectExtending.Modularity;
using Volo.Abp.Security;
using Volo.Abp.Validation;
using Volo.Abp.Threading;

namespace Volo.Abp.IdentityServer
{
    [DependsOn(
        typeof(AbpIdentityServerDomainSharedModule),
        typeof(AbpAutoMapperModule),
        typeof(AbpIdentityDomainModule),
        typeof(AbpSecurityModule),
        typeof(AbpCachingModule),
        typeof(AbpValidationModule),
        typeof(AbpBackgroundWorkersModule)
        )]
    public class AbpIdentityServerDomainModule : AbpModule
    {
        private static readonly OneTimeRunner OneTimeRunner = new OneTimeRunner();

        public override void ConfigureServices(ServiceConfigurationContext context)
        {
            context.Services.AddAutoMapperObjectMapper<AbpIdentityServerDomainModule>();

            Configure<AbpAutoMapperOptions>(options =>
            {
                options.AddProfile<IdentityServerAutoMapperProfile>(validate: true);
            });

            Configure<AbpDistributedEntityEventOptions>(options =>
            {
                options.EtoMappings.Add<ApiResource, ApiResourceEto>(typeof(AbpIdentityServerDomainModule));
                options.EtoMappings.Add<Client, ClientEto>(typeof(AbpIdentityServerDomainModule));
                options.EtoMappings.Add<DeviceFlowCodes, DeviceFlowCodesEto>(typeof(AbpIdentityServerDomainModule));
                options.EtoMappings.Add<IdentityResource, IdentityResourceEto>(typeof(AbpIdentityServerDomainModule));
            });

            AddIdentityServer(context.Services);
        }

        private static void AddIdentityServer(IServiceCollection services)
        {
            var configuration = services.GetConfiguration();
            var builderOptions = services.ExecutePreConfiguredActions<AbpIdentityServerBuilderOptions>();

            var identityServerBuilder = services.AddIdentityServer(options =>
            {
                options.Events.RaiseErrorEvents = true;
                options.Events.RaiseInformationEvents = true;
                options.Events.RaiseFailureEvents = true;
                options.Events.RaiseSuccessEvents = true;
            });

            if (builderOptions.AddDeveloperSigningCredential)
            {
                identityServerBuilder = identityServerBuilder.AddDeveloperSigningCredential();
            }

            identityServerBuilder.AddAbpIdentityServer(builderOptions);

            services.ExecutePreConfiguredActions(identityServerBuilder);

            if (!services.IsAdded<IPersistedGrantService>())
            {
                services.TryAddSingleton<IPersistedGrantStore, InMemoryPersistedGrantStore>();
            }

            if (!services.IsAdded<IDeviceFlowStore>())
            {
                services.TryAddSingleton<IDeviceFlowStore, InMemoryDeviceFlowStore>();
            }

            if (!services.IsAdded<IClientStore>())
            {
                identityServerBuilder.AddInMemoryClients(configuration.GetSection("IdentityServer:Clients"));
            }

            if (!services.IsAdded<IResourceStore>())
            {
                identityServerBuilder.AddInMemoryApiResources(configuration.GetSection("IdentityServer:ApiResources"));
                identityServerBuilder.AddInMemoryIdentityResources(configuration.GetSection("IdentityServer:IdentityResources"));
            }

            if (builderOptions.EnableCaching)
            {
                identityServerBuilder.Services.AddSingleton(typeof(IdentityServerDistributedCache<>));
                identityServerBuilder.Services
                    .AddSingleton<ICache<IdentityServer4.Models.Client>>(provider =>
                    provider.GetRequiredService<IdentityServerDistributedCache<IdentityServer4.Models.Client>>());

                identityServerBuilder.Services
                    .AddSingleton<ICache<IdentityServer4.Models.Resources>>(provider =>
                        provider.GetRequiredService<IdentityServerDistributedCache<IdentityServer4.Models.Resources>>());

                identityServerBuilder.Services
                    .AddSingleton<ICache<IEnumerable<IdentityServer4.Models.IdentityResource>>>(provider =>
                        provider.GetRequiredService<IdentityServerDistributedCache<IEnumerable<IdentityServer4.Models.IdentityResource>>>());

                identityServerBuilder.Services
                    .AddSingleton<ICache<IEnumerable<IdentityServer4.Models.ApiResource>>>(
                    provider => provider.GetRequiredService<IdentityServerDistributedCache<IEnumerable<IdentityServer4.Models.ApiResource>>>());

                identityServerBuilder.Services
                    .AddSingleton<ICache<IEnumerable<IdentityServer4.Models.ApiScope>>>(
                    provider => provider.GetRequiredService<IdentityServerDistributedCache<IEnumerable<IdentityServer4.Models.ApiScope>>>());

                identityServerBuilder.Services
                    .AddSingleton<ICache<CachingCorsPolicyService<AbpCorsPolicyService>.CorsCacheEntry>>(
                        provider => provider.GetRequiredService<IdentityServerDistributedCache<CachingCorsPolicyService<AbpCorsPolicyService>.CorsCacheEntry>>());

                identityServerBuilder.AddClientStoreCache<ClientStore>();
                identityServerBuilder.AddResourceStoreCache<ResourceStore>();
                identityServerBuilder.AddCorsPolicyCache<AbpCorsPolicyService>();
                identityServerBuilder.Services.AddTransient(typeof(IdentityServerCacheItemInvalidator));
            }

            identityServerBuilder.AddExtensionGrantValidator<LinkLoginExtensionGrantValidator>();
        }

        public override void PostConfigureServices(ServiceConfigurationContext context)
        {
            OneTimeRunner.Run(() =>
            {
                ModuleExtensionConfigurationHelper.ApplyEntityConfigurationToEntity(
                    IdentityServerModuleExtensionConsts.ModuleName,
                    IdentityServerModuleExtensionConsts.EntityNames.Client,
                    typeof(Client)
                );

                ModuleExtensionConfigurationHelper.ApplyEntityConfigurationToEntity(
                    IdentityServerModuleExtensionConsts.ModuleName,
                    IdentityServerModuleExtensionConsts.EntityNames.IdentityResource,
                    typeof(IdentityResource)
                );

                ModuleExtensionConfigurationHelper.ApplyEntityConfigurationToEntity(
                    IdentityServerModuleExtensionConsts.ModuleName,
                    IdentityServerModuleExtensionConsts.EntityNames.ApiResource,
                    typeof(ApiResource)
                );
            });
        }

        public override void OnApplicationInitialization(ApplicationInitializationContext context)
        {
            var options = context.ServiceProvider.GetRequiredService<IOptions<TokenCleanupOptions>>().Value;
            if (options.IsCleanupEnabled)
            {
                context.ServiceProvider
                    .GetRequiredService<IBackgroundWorkerManager>()
                    .Add(
                        context.ServiceProvider
                            .GetRequiredService<TokenCleanupBackgroundWorker>()
                    );
            }
        }
    }
}
