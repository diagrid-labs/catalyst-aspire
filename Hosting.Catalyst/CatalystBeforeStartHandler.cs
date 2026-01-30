using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aspire.Hosting.ApplicationModel;
using Diagrid.Aspire.Hosting.Catalyst.Logo;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Diagrid.Aspire.Hosting.Catalyst;

internal class CatalystBeforeStartHandler
{
    private readonly ResourceNotificationService notifications;
    private readonly CatalystProvisioner provisioner;
    private readonly CatalystProject catalystProject;
    private readonly ILogger logger;
    private readonly string projectName;

    public CatalystBeforeStartHandler(BeforeStartEvent beforeStartEvent)
    {
        notifications = beforeStartEvent.Services.GetRequiredService<ResourceNotificationService>();
        var applicationModel1 = beforeStartEvent.Services.GetRequiredService<DistributedApplicationModel>();
        provisioner = beforeStartEvent.Services.GetRequiredService<CatalystProvisioner>();

        catalystProject = applicationModel1.Resources.Single((resource) => resource is CatalystProject)
            as CatalystProject ?? throw new("Huh?");
        logger = beforeStartEvent.Services.GetRequiredService<ResourceLoggerService>()
            .GetLogger(catalystProject);

        projectName = catalystProject.ProjectName;
    }

    public Task EnsureCatalystProvisioning(CancellationToken cancellationToken)
    {
        // todo: This is going to run after the event completes so that it doesn't hang AppHost start.
        _ = Task.Run(async () => {
            
            using var runawayCancellationSource = new CancellationTokenSource();

            try
            {
                await ProvisionCatalystAsync(runawayCancellationSource.Token);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "");
            }
        });

        return Task.CompletedTask;
    }

    private async Task ProvisionCatalystAsync(CancellationToken cancellationToken)
    {
        LogWelcomeMessage();

        await notifications.PublishUpdateAsync(catalystProject, (previous) => previous with
        {
            State = new(KnownResourceStates.Starting, KnownResourceStateStyles.Info),
        });

        await InitializeProvisionerAsync(cancellationToken);
        await EnsureProjectAsync(cancellationToken);
        await SelectProjectAsync(cancellationToken);
        await LoadProjectDetailsAsync(cancellationToken);
        await EnsureApplicationsAsync(cancellationToken);
        await EnsureServicesAsync(cancellationToken, cancellationToken);
        await EnsureComponentsAsync(cancellationToken);
        await CompleteProvisioningAsync();
    }

    private void LogWelcomeMessage()
    {
        logger.LogInformation("\n" + LogoPicker.PickRandomLogo() + "\n");
        logger.LogInformation("Welcome to the Catalyst Aspire integration!");
        logger.LogInformation("Hang on as your environment is provisioned...");
    }

    private async Task InitializeProvisionerAsync(CancellationToken cancellationToken)
    {
        try
        {
            await provisioner.Init(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ex.Message);

            await notifications.PublishUpdateAsync(catalystProject, (previous) => previous with
            {
                State = new(KnownResourceStates.FailedToStart, KnownResourceStateStyles.Error),
            });

            throw;
        }
    }

    private async Task EnsureProjectAsync(CancellationToken cancellationToken)
    {
        await notifications.PublishUpdateAsync(catalystProject, (previous) => previous with
        {
            State = new("Ensuring project", KnownResourceStateStyles.Info),
        });

        try
        {
            await provisioner.CreateProject(projectName, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ex.Message);

            await notifications.PublishUpdateAsync(catalystProject, (previous) => previous with
            {
                State = new(KnownResourceStates.FailedToStart, KnownResourceStateStyles.Error),
            });

            throw;
        }
    }

    private async Task SelectProjectAsync(CancellationToken cancellationToken)
    {
        await notifications.PublishUpdateAsync(catalystProject, (previous) => previous with
        {
            State = new("Selecting project", KnownResourceStateStyles.Info),
        });

        try
        {
            await provisioner.UseProject(projectName, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ex.Message);

            await notifications.PublishUpdateAsync(catalystProject, (previous) => previous with
            {
                State = new(KnownResourceStates.FailedToStart, KnownResourceStateStyles.Error),
            });

            throw;
        }
    }

    private async Task LoadProjectDetailsAsync(CancellationToken cancellationToken)
    {
        await notifications.PublishUpdateAsync(catalystProject, (previous) => previous with
        {
            State = new("Loading project details", KnownResourceStateStyles.Info),
        });

        try
        {
            var projectDetails = await provisioner.GetProjectDetails(projectName, cancellationToken);

            catalystProject.HttpEndpoint.SetResult(projectDetails.HttpEndpoint.ToString());
            catalystProject.GrpcEndpoint.SetResult(projectDetails.GrpcEndpoint.ToString());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ex.Message);

            await notifications.PublishUpdateAsync(catalystProject, (previous) => previous with
            {
                State = new(KnownResourceStates.FailedToStart, KnownResourceStateStyles.Error),
            });

            throw;
        }
    }

    private async Task EnsureApplicationsAsync(CancellationToken cancellationToken)
    {
        await notifications.PublishUpdateAsync(catalystProject, (previous) => previous with
        {
            State = new("Ensuring applications", KnownResourceStateStyles.Info),
        });

        foreach (var pair in catalystProject.AppDetails)
        {
            try
            {
                await provisioner.CreateApp(pair.Key.Name, cancellationToken);
                var app = await provisioner.GetAppDetails(pair.Key.Name, cancellationToken);

                pair.Value.SetResult(app);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, ex.Message);

                await notifications.PublishUpdateAsync(catalystProject, (previous) => previous with
                {
                    State = new(KnownResourceStates.FailedToStart, KnownResourceStateStyles.Error),
                });

                throw;
            }
        }
    }

    private async Task EnsureServicesAsync(
        CancellationToken originalCancellationToken,
        CancellationToken cancellationToken
    )
    {
        await notifications.PublishUpdateAsync(catalystProject, (previous) => previous with
        {
            State = new("Ensuring services", KnownResourceStateStyles.Info),
        });

        foreach (var pair in catalystProject.PubSubs)
        {
            try
            {
                await provisioner.CreatePubSub(pair.Key, pair.Value, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, ex.Message);

                await notifications.PublishUpdateAsync(catalystProject, (previous) => previous with
                {
                    State = new(KnownResourceStates.FailedToStart, KnownResourceStateStyles.Error),
                });

                throw;
            }
        }

        foreach (var pair in catalystProject.KvStores)
        {
            try
            {
                if (await provisioner.CheckKvStoreExists(pair.Key, projectName, originalCancellationToken)) continue;

                await provisioner.CreateKvStore(pair.Key, pair.Value, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, ex.Message);

                await notifications.PublishUpdateAsync(catalystProject, (previous) => previous with
                {
                    State = new(KnownResourceStates.FailedToStart, KnownResourceStateStyles.Error),
                });

                throw;
            }
        }
    }

    private async Task EnsureComponentsAsync(CancellationToken cancellationToken)
    {
        await notifications.PublishUpdateAsync(catalystProject, (previous) => previous with
        {
            State = new("Ensuring components", KnownResourceStateStyles.Info),
        });

        foreach (var pair in catalystProject.Components)
        {
            try
            {
                await provisioner.CreateComponent(pair.Value, projectName, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, ex.Message);

                await notifications.PublishUpdateAsync(catalystProject, (previous) => previous with
                {
                    State = new(KnownResourceStates.FailedToStart, KnownResourceStateStyles.Error),
                });

                throw;
            }
        }
    }

    private async Task CompleteProvisioningAsync()
    {
        await notifications.PublishUpdateAsync(catalystProject, (previous) => previous with
        {
            State = new(KnownResourceStates.Finished, KnownResourceStateStyles.Success),
        });

        logger.LogInformation("Catalyst has been successfully initialized!");
        
        await notifications.PublishUpdateAsync(catalystProject, (previous) => previous with
        {
            State = new(KnownResourceStates.Active, KnownResourceStateStyles.Success),
        });
    }
}
