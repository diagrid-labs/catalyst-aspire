using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Aspire.Hosting.ApplicationModel;
using Diagrid.Aspire.Hosting.Catalyst.Logo;
using Diagrid.Aspire.Hosting.Catalyst.Model;
using Microsoft.Extensions.Logging;

namespace Diagrid.Aspire.Hosting.Catalyst;

internal sealed class CatalystLifecycleHandler(
    CatalystProject catalystProject,
    ResourceLoggerService resourceLogger,
    ResourceNotificationService notifications,
    ICatalystProvisioner provisioner
)
{
    /// <summary>The component type that fronts a Catalyst-managed KV store, and the metadata key naming the store.</summary>
    internal const string ManagedStateStoreType = "state.diagrid";
    internal const string ManagedStateInfraKey = "state";

    /// <summary>The component type that fronts a Catalyst-managed pub/sub broker, and the key naming the broker.</summary>
    internal const string ManagedPubSubType = "pubsub.diagrid";
    internal const string ManagedPubSubInfraKey = "pubsub";

    internal const string CheckingCliState = "Checking Diagrid CLI";
    internal const string WaitingForProjectState = "Waiting for project";
    internal static readonly TimeSpan ProjectReadyTimeout = TimeSpan.FromMinutes(5);

    private readonly ILogger _logger = resourceLogger.GetLogger(catalystProject);

    public async Task ProvisionCatalystAsync(CancellationToken cancellationToken)
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
        await EnsureServicesAsync(cancellationToken);
        await EnsureComponentsAsync(cancellationToken);
        await EnsureSubscriptionsAsync(cancellationToken);
        await CompleteProvisioningAsync();
    }

    /// <summary>
    ///     Faults every still-pending endpoint and identity TCS so anything awaiting them fails fast
    ///     instead of hanging forever when provisioning fails part-way.
    /// </summary>
    private void FaultPending(Exception ex)
    {
        catalystProject.HttpEndpoint.TrySetException(ex);
        catalystProject.GrpcEndpoint.TrySetException(ex);

        foreach (var identity in catalystProject.Identities.Values)
        {
            identity.Details.TrySetException(ex);
        }
    }

    private void LogWelcomeMessage()
    {
        // A constant template with the art as an argument: concatenating it in would make every run its own
        // template, and ASCII art is exactly the kind of string that can contain brace characters.
        // Guarded because PickRandomLogo reads an embedded resource, which is wasted work if Information is off.
        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("{Logo}", "\n" + LogoPicker.PickRandomLogo() + "\n");
        }

        _logger.LogInformation("Welcome to the Catalyst Aspire integration!");
        _logger.LogInformation("Hang on as your environment is provisioned...");
    }

    private async Task InitializeProvisionerAsync(CancellationToken cancellationToken)
    {
        await notifications.PublishUpdateAsync(catalystProject, (previous) => previous with
        {
            State = new(CheckingCliState, KnownResourceStateStyles.Info),
        });

        try
        {
            await provisioner.InitializeAsync(_logger, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Ctrl-C is not a failure: no error, no stack trace, no FailedToStart.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Message}", ex.Message);
            FaultPending(ex);

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
            await provisioner.CreateProjectAsync(catalystProject.ProjectName, catalystProject.ProjectDescriptor, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Ctrl-C is not a failure: no error, no stack trace, no FailedToStart.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Message}", ex.Message);
            FaultPending(ex);

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
            await provisioner.UseProjectAsync(catalystProject.ProjectName, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Ctrl-C is not a failure: no error, no stack trace, no FailedToStart.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Message}", ex.Message);
            FaultPending(ex);

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
            // A project that already existed may still be starting (project create returns early for it).
            await notifications.PublishUpdateAsync(catalystProject, (previous) => previous with
            {
                State = new(WaitingForProjectState, KnownResourceStateStyles.Info),
            });

            var projectDetails = await provisioner.WaitForProjectReadyAsync(
                catalystProject.ProjectName, ProjectReadyTimeout, cancellationToken);

            catalystProject.HttpEndpoint.SetResult(projectDetails.HttpEndpoint.ToString());
            catalystProject.GrpcEndpoint.SetResult(projectDetails.GrpcEndpoint.ToString());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Ctrl-C is not a failure: no error, no stack trace, no FailedToStart.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Message}", ex.Message);
            FaultPending(ex);

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
            State = new("Ensuring identities", KnownResourceStateStyles.Info),
        });

        foreach (var pair in catalystProject.Identities)
        {
            try
            {
                var name = pair.Key.Name;
                var project = catalystProject.ProjectName;

                switch (pair.Value.Kind)
                {
                    case IdentityKind.Agent:
                        await provisioner.CreateAgentAsync(name, project, cancellationToken);
                        pair.Value.Details.SetResult(await provisioner.GetAgentDetailsAsync(name, project, cancellationToken));
                        break;
                    default:
                        await provisioner.CreateAppAsync(name, project, cancellationToken);
                        pair.Value.Details.SetResult(await provisioner.GetAppDetailsAsync(name, project, cancellationToken));
                        break;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Ctrl-C is not a failure: no error, no stack trace, no FailedToStart.
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Message}", ex.Message);
                FaultPending(ex);

                await notifications.PublishUpdateAsync(catalystProject, (previous) => previous with
                {
                    State = new(KnownResourceStates.FailedToStart, KnownResourceStateStyles.Error),
                });

                throw;
            }
        }
    }

    private async Task EnsureServicesAsync(CancellationToken cancellationToken)
    {
        await notifications.PublishUpdateAsync(catalystProject, (previous) => previous with
        {
            State = new("Ensuring services", KnownResourceStateStyles.Info),
        });

        foreach (var pair in catalystProject.PubSubs)
        {
            try
            {
                await provisioner.CreatePubSubAsync(pair.Key, pair.Value, cancellationToken);
                await EnsureManagedServiceComponentAsync(
                    pair.Key, ManagedPubSubType, ManagedPubSubInfraKey, pair.Value.Scopes, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Ctrl-C is not a failure: no error, no stack trace, no FailedToStart.
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Message}", ex.Message);
                FaultPending(ex);

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
                await provisioner.CreateKvStoreAsync(pair.Key, pair.Value, cancellationToken);
                await EnsureManagedServiceComponentAsync(
                    pair.Key, ManagedStateStoreType, ManagedStateInfraKey, pair.Value.Scopes, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Ctrl-C is not a failure: no error, no stack trace, no FailedToStart.
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Message}", ex.Message);
                FaultPending(ex);

                await notifications.PublishUpdateAsync(catalystProject, (previous) => previous with
                {
                    State = new(KnownResourceStates.FailedToStart, KnownResourceStateStyles.Error),
                });

                throw;
            }
        }
    }

    /// <summary>
    ///     Makes sure a declared managed service has a component named after it, which is how an app addresses it.
    /// </summary>
    /// <remarks>
    ///     <c>kv create</c> / <c>pubsub create</c> normally create that component themselves, so this is usually
    ///     a no-op that <c>--ignore-if-exists</c> absorbs. It is not a no-op when the service already existed
    ///     WITHOUT one, and creating a project's first agent is exactly that case: the control plane's
    ///     <c>EnsureAgentRegistry</c> and <c>ensureAgentPubsub</c> provision a managed KV store named
    ///     <c>kvstore</c> and a managed broker named <c>pubsub</c> with <c>WithCreateComponent(false)</c> — the
    ///     same two names <see cref="ResourceBuilderExtensions.SampleKvName"/> and
    ///     <see cref="ResourceBuilderExtensions.SamplePubsubName"/> default to. A later create is then refused as
    ///     a conflict, the CLI's <c>--ignore-if-exists</c> turns that into success, and the component is never
    ///     made: provisioning goes green and the app fails at runtime with a missing component.
    ///     <br /><br />
    ///     Skipped when the AppHost declares its own component for the service: that one is reconciled by
    ///     <see cref="EnsureComponentsAsync"/> with the developer's spec, and creating a default first would be
    ///     a duplicate.
    /// </remarks>
    /// <param name="name">The managed service's name, which the component takes too.</param>
    /// <param name="type">The fronting component's type.</param>
    /// <param name="infraMetadataKey">The metadata key naming the managed service the component fronts.</param>
    /// <param name="scopes">The service's scopes, carried to the component.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    private async Task EnsureManagedServiceComponentAsync(
        string name,
        string type,
        string infraMetadataKey,
        IList<string> scopes,
        CancellationToken cancellationToken
    )
    {
        if (catalystProject.Components.ContainsKey(name))
        {
            return;
        }

        var descriptor = new ComponentDescriptor
        {
            Name = name,
            Type = type,
            Scopes = scopes,
            Metadata = new Dictionary<string, object?> { [infraMetadataKey] = name },
        };

        await provisioner.UpsertComponentAsync(descriptor, catalystProject.ProjectName, isManagedServiceComponent: false, cancellationToken);
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
                // A component named after a declared KV store or pub/sub is the one the CLI created for it: update, do not create.
                var backsDeclaredService = catalystProject.KvStores.ContainsKey(pair.Value.Name)
                                           || catalystProject.PubSubs.ContainsKey(pair.Value.Name);

                await provisioner.UpsertComponentAsync(pair.Value, catalystProject.ProjectName, backsDeclaredService, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Ctrl-C is not a failure: no error, no stack trace, no FailedToStart.
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Message}", ex.Message);
                FaultPending(ex);

                await notifications.PublishUpdateAsync(catalystProject, (previous) => previous with
                {
                    State = new(KnownResourceStates.FailedToStart, KnownResourceStateStyles.Error),
                });

                throw;
            }
        }
    }

    private async Task EnsureSubscriptionsAsync(CancellationToken cancellationToken)
    {
        await notifications.PublishUpdateAsync(catalystProject, (previous) => previous with
        {
            State = new("Ensuring subscriptions", KnownResourceStateStyles.Info),
        });

        foreach (var pair in catalystProject.Subscriptions)
        {
            try
            {
                await provisioner.UpsertSubscriptionAsync(catalystProject.ProjectName, pair.Value, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Ctrl-C is not a failure: no error, no stack trace, no FailedToStart.
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Message}", ex.Message);
                FaultPending(ex);

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

        _logger.LogInformation("Catalyst has been successfully initialized!");

        await notifications.PublishUpdateAsync(catalystProject, (previous) => previous with
        {
            State = new(KnownResourceStates.Active, KnownResourceStateStyles.Success),
        });
    }
}
