using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using AsyncAwaitBestPractices;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using DynamicData;
using DynamicData.Binding;
using Injectio.Attributes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Refit;
using SkiaSharp;
using StabilityMatrix.Avalonia.Helpers;
using StabilityMatrix.Avalonia.Models;
using StabilityMatrix.Avalonia.Models.TagCompletion;
using StabilityMatrix.Core.Api;
using StabilityMatrix.Core.Extensions;
using StabilityMatrix.Core.Helper;
using StabilityMatrix.Core.Inference;
using StabilityMatrix.Core.Models;
using StabilityMatrix.Core.Models.Api.Comfy;
using StabilityMatrix.Core.Models.Configs;
using StabilityMatrix.Core.Models.FileInterfaces;
using StabilityMatrix.Core.Models.Packages;
using StabilityMatrix.Core.Services;

namespace StabilityMatrix.Avalonia.Services;

/// <summary>
/// Manager for the current inference client
/// Has observable shared properties for shared info like model names
/// </summary>
[RegisterSingleton<IInferenceClientManager, InferenceClientManager>]
public partial class InferenceClientManager : ObservableObject, IInferenceClientManager
{
    private readonly ILogger<InferenceClientManager> logger;
    private readonly IApiFactory apiFactory;
    private readonly IModelIndexService modelIndexService;
    private readonly ISettingsManager settingsManager;
    private readonly ICompletionProvider completionProvider;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnected), nameof(CanUserConnect))]
    private ComfyClient? client;

    [MemberNotNullWhen(true, nameof(Client))]
    public virtual bool IsConnected => Client is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUserConnect))]
    private bool isConnecting;

    /// <inheritdoc />
    public bool CanUserConnect => !IsConnected && !IsConnecting;

    /// <inheritdoc />
    public bool CanUserDisconnect => IsConnected && !IsConnecting;

    private readonly SourceCache<HybridModelFile, string> modelsSource = new(p => p.GetId());

    public IObservableCollection<HybridModelFile> Models { get; } =
        new ObservableCollectionExtended<HybridModelFile>();

    private readonly SourceCache<HybridModelFile, string> vaeModelsSource = new(p => p.GetId());

    private readonly SourceCache<HybridModelFile, string> vaeModelsDefaults = new(p => p.GetId());

    public IObservableCollection<HybridModelFile> VaeModels { get; } =
        new ObservableCollectionExtended<HybridModelFile>();

    private readonly SourceCache<HybridModelFile, string> controlNetModelsSource = new(p => p.GetId());

    private readonly SourceCache<HybridModelFile, string> downloadableControlNetModelsSource = new(p =>
        p.GetId()
    );

    public IObservableCollection<HybridModelFile> ControlNetModels { get; } =
        new ObservableCollectionExtended<HybridModelFile>();

    private readonly SourceCache<HybridModelFile, string> loraModelsSource = new(p => p.GetId());

    public IObservable<IChangeSet<HybridModelFile, string>> LoraModelsChangeSet { get; }

    public IObservableCollection<HybridModelFile> LoraModels { get; } =
        new ObservableCollectionExtended<HybridModelFile>();

    private readonly SourceCache<HybridModelFile, string> promptExpansionModelsSource = new(p => p.GetId());

    private readonly SourceCache<HybridModelFile, string> downloadablePromptExpansionModelsSource = new(p =>
        p.GetId()
    );

    public IObservableCollection<HybridModelFile> PromptExpansionModels { get; } =
        new ObservableCollectionExtended<HybridModelFile>();

    private readonly SourceCache<ComfySampler, string> samplersSource = new(p => p.Name);

    public IObservableCollection<ComfySampler> Samplers { get; } =
        new ObservableCollectionExtended<ComfySampler>();

    private readonly SourceCache<ComfyUpscaler, string> modelUpscalersSource = new(p => p.Name);

    private readonly SourceCache<ComfyUpscaler, string> latentUpscalersSource = new(p => p.Name);

    private readonly SourceCache<ComfyUpscaler, string> downloadableUpscalersSource = new(p => p.Name);

    public IObservableCollection<ComfyUpscaler> Upscalers { get; } =
        new ObservableCollectionExtended<ComfyUpscaler>();

    private readonly SourceCache<ComfyScheduler, string> schedulersSource = new(p => p.Name);

    public IObservableCollection<ComfyScheduler> Schedulers { get; } =
        new ObservableCollectionExtended<ComfyScheduler>();

    public IObservableCollection<ComfyAuxPreprocessor> Preprocessors { get; } =
        new ObservableCollectionExtended<ComfyAuxPreprocessor>();

    private readonly SourceCache<ComfyAuxPreprocessor, string> preprocessorsSource = new(p => p.Value);

    public IObservableCollection<HybridModelFile> UltralyticsModels { get; } =
        new ObservableCollectionExtended<HybridModelFile>();

    private readonly SourceCache<HybridModelFile, string> ultralyticsModelsSource = new(p => p.GetId());

    private readonly SourceCache<HybridModelFile, string> downloadableUltralyticsModelsSource = new(p =>
        p.GetId()
    );

    public IObservableCollection<HybridModelFile> SamModels { get; } =
        new ObservableCollectionExtended<HybridModelFile>();

    private readonly SourceCache<HybridModelFile, string> samModelsSource = new(p => p.GetId());

    private readonly SourceCache<HybridModelFile, string> downloadableSamModelsSource = new(p => p.GetId());

    private readonly SourceCache<HybridModelFile, string> unetModelsSource = new(p => p.GetId());

    public IObservableCollection<HybridModelFile> UnetModels { get; } =
        new ObservableCollectionExtended<HybridModelFile>();

    private readonly SourceCache<HybridModelFile, string> clipModelsSource = new(p => p.GetId());
    private readonly SourceCache<HybridModelFile, string> downloadableClipModelsSource = new(p => p.GetId());

    public IObservableCollection<HybridModelFile> ClipModels { get; } =
        new ObservableCollectionExtended<HybridModelFile>();

    private readonly SourceCache<HybridModelFile, string> clipVisionModelsSource = new(p => p.GetId());
    private readonly SourceCache<HybridModelFile, string> downloadableClipVisionModelsSource = new(p =>
        p.GetId()
    );

    public IObservableCollection<HybridModelFile> ClipVisionModels { get; } =
        new ObservableCollectionExtended<HybridModelFile>();

    public InferenceClientManager(
        ILogger<InferenceClientManager> logger,
        IApiFactory apiFactory,
        IModelIndexService modelIndexService,
        ISettingsManager settingsManager,
        ICompletionProvider completionProvider
    )
    {
        this.logger = logger;
        this.apiFactory = apiFactory;
        this.modelIndexService = modelIndexService;
        this.settingsManager = settingsManager;
        this.completionProvider = completionProvider;

        modelsSource
            .Connect()
            .DeferUntilLoaded()
            .SortAndBind(Models, SortExpressionComparer<HybridModelFile>.Ascending(f => f.ShortDisplayName))
            .ObserveOn(SynchronizationContext.Current)
            .Subscribe();

        controlNetModelsSource
            .Connect()
            .Or(downloadableControlNetModelsSource.Connect())
            .Sort(
                SortExpressionComparer<HybridModelFile>
                    .Ascending(f => f.Type)
                    .ThenByAscending(f => f.ShortDisplayName)
            )
            .DeferUntilLoaded()
            .Bind(ControlNetModels)
            .ObserveOn(SynchronizationContext.Current)
            .Subscribe();

        LoraModelsChangeSet = loraModelsSource
            .Connect()
            .DeferUntilLoaded()
            // Adding .RefCount() if multiple consumers might subscribe to this
            // LoraModelsChangeSet property. It keeps the upstream connection active as long
            // as there's at least one subscriber. This is usually a good idea when exposing streams.
            .RefCount();

        LoraModelsChangeSet
            .SortAndBind(
                LoraModels,
                SortExpressionComparer<HybridModelFile>.Ascending(f => f.Type).ThenByAscending(f => f.SortKey)
            )
            .ObserveOn(SynchronizationContext.Current)
            .Subscribe();

        promptExpansionModelsSource
            .Connect()
            .Or(downloadablePromptExpansionModelsSource.Connect())
            .Sort(
                SortExpressionComparer<HybridModelFile>
                    .Ascending(f => f.Type)
                    .ThenByAscending(f => f.ShortDisplayName)
            )
            .DeferUntilLoaded()
            .Bind(PromptExpansionModels)
            .ObserveOn(SynchronizationContext.Current)
            .Subscribe();

        ultralyticsModelsSource
            .Connect()
            .Or(downloadableUltralyticsModelsSource.Connect())
            .Sort(
                SortExpressionComparer<HybridModelFile>
                    .Ascending(f => f.Type)
                    .ThenByAscending(f => f.ShortDisplayName)
            )
            .DeferUntilLoaded()
            .Bind(UltralyticsModels)
            .ObserveOn(SynchronizationContext.Current)
            .Subscribe();

        samModelsSource
            .Connect()
            .Or(downloadableSamModelsSource.Connect())
            .Sort(
                SortExpressionComparer<HybridModelFile>
                    .Ascending(f => f.Type)
                    .ThenByAscending(f => f.ShortDisplayName)
            )
            .DeferUntilLoaded()
            .Bind(SamModels)
            .ObserveOn(SynchronizationContext.Current)
            .Subscribe();

        unetModelsSource
            .Connect()
            .DeferUntilLoaded()
            .SortAndBind(
                UnetModels,
                SortExpressionComparer<HybridModelFile>.Ascending(f => f.ShortDisplayName)
            )
            .ObserveOn(SynchronizationContext.Current)
            .Subscribe();

        clipModelsSource
            .Connect()
            .Or(downloadableClipModelsSource.Connect())
            .SortBy(
                f => f.ShortDisplayName,
                SortDirection.Ascending,
                SortOptimisations.ComparesImmutableValuesOnly
            )
            .DeferUntilLoaded()
            .Bind(ClipModels)
            .ObserveOn(SynchronizationContext.Current)
            .Subscribe();

        clipVisionModelsSource
            .Connect()
            .Or(downloadableClipVisionModelsSource.Connect())
            .SortBy(
                f => f.ShortDisplayName,
                SortDirection.Ascending,
                SortOptimisations.ComparesImmutableValuesOnly
            )
            .DeferUntilLoaded()
            .Bind(ClipVisionModels)
            .ObserveOn(SynchronizationContext.Current)
            .Subscribe();

        vaeModelsDefaults.AddOrUpdate(HybridModelFile.Default);

        vaeModelsDefaults
            .Connect()
            .Or(vaeModelsSource.Connect())
            .Bind(VaeModels)
            .ObserveOn(SynchronizationContext.Current)
            .Subscribe();

        samplersSource
            .Connect()
            .DeferUntilLoaded()
            .Bind(Samplers)
            .ObserveOn(SynchronizationContext.Current)
            .Subscribe();

        latentUpscalersSource
            .Connect()
            .Or(modelUpscalersSource.Connect())
            .Or(downloadableUpscalersSource.Connect())
            .Sort(SortExpressionComparer<ComfyUpscaler>.Ascending(f => f.Type).ThenByAscending(f => f.Name))
            .Bind(Upscalers)
            .ObserveOn(SynchronizationContext.Current)
            .Subscribe();

        schedulersSource
            .Connect()
            .DeferUntilLoaded()
            .Bind(Schedulers)
            .ObserveOn(SynchronizationContext.Current)
            .Subscribe();

        preprocessorsSource
            .Connect()
            .DeferUntilLoaded()
            .Bind(Preprocessors)
            .ObserveOn(SynchronizationContext.Current)
            .Subscribe();

        settingsManager.RegisterOnLibraryDirSet(_ =>
        {
            Dispatcher.UIThread.Post(ResetSharedProperties, DispatcherPriority.Background);
        });

        EventManager.Instance.ModelIndexChanged += (_, _) =>
        {
            logger.LogDebug("Model index changed, reloading shared properties for Inference");

            if (!settingsManager.IsLibraryDirSet)
                return;

            ResetSharedProperties();

            if (IsConnected)
            {
                LoadSharedPropertiesAsync()
                    .SafeFireAndForget(onException: ex =>
                        logger.LogError(ex, "Error loading shared properties")
                    );
            }
        };
    }

    [MemberNotNull(nameof(Client))]
    private void EnsureConnected()
    {
        if (!IsConnected)
            throw new InvalidOperationException("Client is not connected");
    }

    /// <summary>
    /// Safely calls an API method and handles HTML responses (common with Cloudflare errors)
    /// </summary>
    private async Task<T?> SafeApiCallAsync<T>(
        Func<Task<T>> apiCall,
        string operationName,
        CancellationToken cancellationToken = default
    )
        where T : class
    {
        try
        {
            logger.LogDebug(
                "Making API call: {Operation} to {BaseAddress}",
                operationName,
                Client.BaseAddress
            );
            var result = await apiCall().ConfigureAwait(false);
            logger.LogDebug("API call succeeded: {Operation}", operationName);
            return result;
        }
        catch (Refit.ApiException apiEx)
        {
            // Check if we were redirected to Cloudflare Access
            var requestUri = apiEx.RequestMessage?.RequestUri?.ToString() ?? "";
            var isCloudflareAccessRedirect = requestUri.Contains(
                "cloudflareaccess.com",
                StringComparison.OrdinalIgnoreCase
            );

            // Check if response is HTML instead of JSON
            if (apiEx.Content is { } content && content.TrimStart().StartsWith("<", StringComparison.Ordinal))
            {
                // Log first 500 chars of HTML response for debugging
                var preview = content.Length > 500 ? content[..500] + "..." : content;

                if (isCloudflareAccessRedirect)
                {
                    logger.LogWarning(
                        apiEx,
                        "Request to {Operation} was redirected to Cloudflare Access login page: {RedirectUri}. "
                            + "This means authentication headers are missing or incorrect. "
                            + "Please check your ComfyUI authentication headers configuration in settings. "
                            + "Cloudflare Access requires specific headers (like CF-Access-Token or CF-Access-Client-Id/Secret) to be set. "
                            + "Response preview: {Preview}",
                        operationName,
                        requestUri,
                        preview
                    );
                }
                else
                {
                    logger.LogWarning(
                        apiEx,
                        "Received HTML response instead of JSON for {Operation} from {Uri}. "
                            + "This usually indicates the server is returning an error page. "
                            + "For Cloudflare tunnels, ensure you're using HTTPS and have proper authentication headers. "
                            + "Response preview: {Preview}",
                        operationName,
                        apiEx.Uri ?? Client.BaseAddress,
                        preview
                    );
                }
                return null;
            }

            // Log other API errors but don't fail the entire connection
            if (isCloudflareAccessRedirect)
            {
                logger.LogWarning(
                    apiEx,
                    "API call failed for {Operation} - redirected to Cloudflare Access login: {RedirectUri}. "
                        + "This indicates authentication headers are missing or incorrect. "
                        + "Status: {StatusCode} {ReasonPhrase}. "
                        + "Original request: {Method} {OriginalUri}",
                    operationName,
                    requestUri,
                    apiEx.StatusCode,
                    apiEx.ReasonPhrase,
                    apiEx.HttpMethod,
                    apiEx.Uri ?? Client.BaseAddress
                );
            }
            else
            {
                logger.LogWarning(
                    apiEx,
                    "API call failed for {Operation} from {Uri}: {StatusCode} {ReasonPhrase}. "
                        + "Request: {Method} {RequestUri}",
                    operationName,
                    apiEx.Uri ?? Client.BaseAddress,
                    apiEx.StatusCode,
                    apiEx.ReasonPhrase,
                    apiEx.HttpMethod,
                    apiEx.RequestMessage?.RequestUri
                );
            }
            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Unexpected error during {Operation} from {Uri}",
                operationName,
                Client.BaseAddress
            );
            return null;
        }
    }

    protected virtual async Task LoadSharedPropertiesAsync()
    {
        EnsureConnected();

        // Get model names
        if (await SafeApiCallAsync(() => Client.GetModelNamesAsync(), "GetModelNames") is { } modelNames)
        {
            modelsSource.EditDiff(
                modelNames.Select(HybridModelFile.FromRemote),
                HybridModelFile.RemoteLocalComparer
            );
        }

        // Get control net model names
        if (
            await SafeApiCallAsync(
                () => Client.GetNodeOptionNamesAsync("ControlNetLoader", "control_net_name"),
                "GetControlNetModelNames"
            ) is
            { } controlNetModelNames
        )
        {
            controlNetModelsSource.EditDiff(
                controlNetModelNames.Select(HybridModelFile.FromRemote),
                HybridModelFile.RemoteLocalComparer
            );
        }

        // Get Lora model names
        if (
            await SafeApiCallAsync(
                () => Client.GetNodeOptionNamesAsync("LoraLoader", "lora_name"),
                "GetLoraModelNames"
            ) is
            { } loraModelNames
        )
        {
            loraModelsSource.EditDiff(
                loraModelNames.Select(HybridModelFile.FromRemote),
                HybridModelFile.RemoteLocalComparer
            );
        }

        // Get Ultralytics model names
        if (
            await SafeApiCallAsync(
                () => Client.GetOptionalNodeOptionNamesAsync("UltralyticsDetectorProvider", "model_name"),
                "GetUltralyticsModelNames"
            ) is
            { } ultralyticsModelNames
        )
        {
            IEnumerable<HybridModelFile> models =
            [
                HybridModelFile.None,
                .. ultralyticsModelNames.Select(HybridModelFile.FromRemote),
            ];
            ultralyticsModelsSource.EditDiff(models, HybridModelFile.RemoteLocalComparer);
        }

        // Get SAM model names
        if (
            await SafeApiCallAsync(
                () => Client.GetOptionalNodeOptionNamesAsync("SAMLoader", "model_name"),
                "GetSamModelNames"
            ) is
            { } samModelNames
        )
        {
            IEnumerable<HybridModelFile> models =
            [
                HybridModelFile.None,
                .. samModelNames.Select(HybridModelFile.FromRemote),
            ];
            samModelsSource.EditDiff(models, HybridModelFile.RemoteLocalComparer);
        }

        // Prompt Expansion indexing is local only

        // Fetch sampler names from KSampler node
        if (
            await SafeApiCallAsync(() => Client.GetSamplerNamesAsync(), "GetSamplerNames") is { } samplerNames
        )
        {
            samplersSource.EditDiff(
                samplerNames.Select(name => new ComfySampler(name)),
                ComfySampler.Comparer
            );
        }

        // Upscalers is latent and esrgan combined

        // Add latent upscale methods from LatentUpscale node
        if (
            await SafeApiCallAsync(
                () => Client.GetNodeOptionNamesAsync("LatentUpscale", "upscale_method"),
                "GetLatentUpscalerNames"
            ) is
            { } latentUpscalerNames
        )
        {
            latentUpscalersSource.EditDiff(
                latentUpscalerNames.Select(s => new ComfyUpscaler(s, ComfyUpscalerType.Latent)),
                ComfyUpscaler.Comparer
            );

            logger.LogTrace("Loaded latent upscale methods: {@Upscalers}", latentUpscalerNames);
        }

        // Add Model upscale methods
        if (
            await SafeApiCallAsync(
                () => Client.GetNodeOptionNamesAsync("UpscaleModelLoader", "model_name"),
                "GetModelUpscalerNames"
            ) is
            { } modelUpscalerNames
        )
        {
            modelUpscalersSource.EditDiff(
                modelUpscalerNames.Select(s => new ComfyUpscaler(s, ComfyUpscalerType.ESRGAN)),
                ComfyUpscaler.Comparer
            );
            logger.LogTrace("Loaded model upscale methods: {@Upscalers}", modelUpscalerNames);
        }

        // Add scheduler names from Scheduler node
        if (
            await SafeApiCallAsync(
                () => Client.GetNodeOptionNamesAsync("KSampler", "scheduler"),
                "GetSchedulerNames"
            ) is
            { } schedulerNames
        )
        {
            schedulersSource.Edit(updater =>
            {
                updater.AddOrUpdate(
                    schedulerNames
                        .Where(n => !schedulersSource.Keys.Contains(n))
                        .Select(s => new ComfyScheduler(s))
                );
            });
            logger.LogTrace("Loaded scheduler methods: {@Schedulers}", schedulerNames);
        }

        // Add preprocessor names from Inference_Core_AIO_Preprocessor node (might not exist if no extension)
        if (
            await SafeApiCallAsync(
                () =>
                    Client.GetOptionalNodeOptionNamesAsync("Inference_Core_AIO_Preprocessor", "preprocessor"),
                "GetPreprocessorNames"
            ) is
            { } preprocessorNames
        )
        {
            preprocessorsSource.EditDiff(preprocessorNames.Select(n => new ComfyAuxPreprocessor(n)));
        }

        // Get Unet model names from UNETLoader node
        if (
            await SafeApiCallAsync(
                () => Client.GetNodeOptionNamesAsync("UNETLoader", "unet_name"),
                "GetUnetModelNames"
            ) is
            { } unetModelNames
        )
        {
            var unetModels = unetModelNames.Select(HybridModelFile.FromRemote);

            if (
                await SafeApiCallAsync(
                    () =>
                        Client.GetRequiredNodeOptionNamesFromOptionalNodeAsync("UnetLoaderGGUF", "unet_name"),
                    "GetUnetGGUFModelNames"
                ) is
                { } ggufModelNames
            )
            {
                unetModels = unetModels.Concat(ggufModelNames.Select(HybridModelFile.FromRemote));
            }

            unetModelsSource.AddOrUpdate(unetModels, HybridModelFile.RemoteLocalComparer);
        }

        // Get CLIP model names from DualCLIPLoader node
        if (
            await SafeApiCallAsync(
                () => Client.GetNodeOptionNamesAsync("DualCLIPLoader", "clip_name1"),
                "GetClipModelNames"
            ) is
            { } clipModelNames
        )
        {
            IEnumerable<HybridModelFile> models =
            [
                HybridModelFile.None,
                .. clipModelNames.Select(HybridModelFile.FromRemote),
            ];

            if (
                await SafeApiCallAsync(
                    () =>
                        Client.GetRequiredNodeOptionNamesFromOptionalNodeAsync(
                            "DualCLIPLoaderGGUF",
                            "clip_name1"
                        ),
                    "GetClipGGUFModelNames"
                ) is
                { } ggufClipModelNames
            )
            {
                models = models.Concat(ggufClipModelNames.Select(HybridModelFile.FromRemote));
            }

            clipModelsSource.EditDiff(models, HybridModelFile.RemoteLocalComparer);
        }

        // Get CLIP Vision model names from CLIPVisionLoader node
        if (
            await SafeApiCallAsync(
                () => Client.GetNodeOptionNamesAsync("CLIPVisionLoader", "clip_name"),
                "GetClipVisionModelNames"
            ) is
            { } clipVisionModelNames
        )
        {
            IEnumerable<HybridModelFile> models =
            [
                HybridModelFile.None,
                .. clipVisionModelNames.Select(HybridModelFile.FromRemote),
            ];
            clipVisionModelsSource.EditDiff(models, HybridModelFile.RemoteLocalComparer);
        }
    }

    /// <summary>
    /// Clears shared properties and sets them to local defaults
    /// </summary>
    protected void ResetSharedProperties()
    {
        // Load local models
        modelsSource.EditDiff(
            modelIndexService
                .FindByModelType(SharedFolderType.StableDiffusion)
                .Select(HybridModelFile.FromLocal),
            HybridModelFile.Comparer
        );

        // Load local control net models
        controlNetModelsSource.EditDiff(
            modelIndexService.FindByModelType(SharedFolderType.ControlNet).Select(HybridModelFile.FromLocal),
            HybridModelFile.Comparer
        );

        // Downloadable ControlNet models
        var downloadableControlNets = RemoteModels.ControlNetModels.Where(u =>
            !controlNetModelsSource.Lookup(u.GetId()).HasValue
        );
        downloadableControlNetModelsSource.EditDiff(downloadableControlNets, HybridModelFile.Comparer);

        // Load local Lora / LyCORIS models
        loraModelsSource.EditDiff(
            modelIndexService
                .FindByModelType(SharedFolderType.Lora | SharedFolderType.LyCORIS)
                .Select(HybridModelFile.FromLocal),
            HybridModelFile.Comparer
        );

        // Load local prompt expansion models
        promptExpansionModelsSource.EditDiff(
            modelIndexService
                .FindByModelType(SharedFolderType.PromptExpansion)
                .Select(HybridModelFile.FromLocal),
            HybridModelFile.Comparer
        );

        // Downloadable PromptExpansion models
        downloadablePromptExpansionModelsSource.EditDiff(
            RemoteModels.PromptExpansionModels.Where(u =>
                !promptExpansionModelsSource.Lookup(u.GetId()).HasValue
            ),
            HybridModelFile.Comparer
        );

        // Load local VAE models
        vaeModelsSource.EditDiff(
            modelIndexService.FindByModelType(SharedFolderType.VAE).Select(HybridModelFile.FromLocal),
            HybridModelFile.Comparer
        );

        // Load Ultralytics models
        IEnumerable<HybridModelFile> ultralyticsModels =
        [
            HybridModelFile.None,
            .. modelIndexService
                .FindByModelType(SharedFolderType.Ultralytics)
                .Select(HybridModelFile.FromLocal),
        ];
        ultralyticsModelsSource.EditDiff(ultralyticsModels, HybridModelFile.Comparer);

        var downloadableUltralyticsModels = RemoteModels.UltralyticsModelFiles.Where(u =>
            !ultralyticsModelsSource.Lookup(u.GetId()).HasValue
        );
        downloadableUltralyticsModelsSource.EditDiff(downloadableUltralyticsModels, HybridModelFile.Comparer);

        // Load SAM models
        IEnumerable<HybridModelFile> samModels =
        [
            HybridModelFile.None,
            .. modelIndexService.FindByModelType(SharedFolderType.Sams).Select(HybridModelFile.FromLocal),
        ];
        samModelsSource.EditDiff(samModels, HybridModelFile.Comparer);

        var downloadableSamModels = RemoteModels.SamModelFiles.Where(u =>
            !samModelsSource.Lookup(u.GetId()).HasValue
        );
        downloadableSamModelsSource.EditDiff(downloadableSamModels, HybridModelFile.Comparer);

        unetModelsSource.EditDiff(
            modelIndexService
                .FindByModelType(SharedFolderType.DiffusionModels)
                .Select(HybridModelFile.FromLocal),
            HybridModelFile.Comparer
        );

        clipModelsSource.EditDiff(
            modelIndexService
                .FindByModelType(SharedFolderType.TextEncoders)
                .Select(HybridModelFile.FromLocal),
            HybridModelFile.Comparer
        );

        var downloadableClipModels = RemoteModels.ClipModelFiles.Where(u =>
            !clipModelsSource.Lookup(u.GetId()).HasValue
        );
        downloadableClipModelsSource.EditDiff(downloadableClipModels, HybridModelFile.Comparer);

        clipVisionModelsSource.EditDiff(
            modelIndexService.FindByModelType(SharedFolderType.ClipVision).Select(HybridModelFile.FromLocal),
            HybridModelFile.Comparer
        );

        var downloadableClipVisionModels = RemoteModels.ClipVisionModelFiles.Where(u =>
            !clipVisionModelsSource.Lookup(u.GetId()).HasValue
        );
        downloadableClipVisionModelsSource.EditDiff(downloadableClipVisionModels, HybridModelFile.Comparer);

        samplersSource.EditDiff(ComfySampler.Defaults, ComfySampler.Comparer);

        latentUpscalersSource.EditDiff(ComfyUpscaler.Defaults, ComfyUpscaler.Comparer);

        schedulersSource.EditDiff(ComfyScheduler.Defaults, ComfyScheduler.Comparer);

        // Load Upscalers
        modelUpscalersSource.EditDiff(
            modelIndexService
                .FindByModelType(
                    SharedFolderType.ESRGAN | SharedFolderType.RealESRGAN | SharedFolderType.SwinIR
                )
                .Select(m => new ComfyUpscaler(m.FileName, ComfyUpscalerType.ESRGAN)),
            ComfyUpscaler.Comparer
        );

        // Remote upscalers
        var remoteUpscalers = ComfyUpscaler.DefaultDownloadableModels.Where(u =>
            !modelUpscalersSource.Lookup(u.Name).HasValue
        );
        downloadableUpscalersSource.EditDiff(remoteUpscalers, ComfyUpscaler.Comparer);

        // Default Preprocessors
        preprocessorsSource.EditDiff(ComfyAuxPreprocessor.Defaults);
    }

    /// <inheritdoc />
    public async Task UploadInputImageAsync(ImageSource image, CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        var uploadName = await image.GetHashGuidFileNameAsync();

        if (image.LocalFile is { } localFile)
        {
            logger.LogDebug("Uploading image {FileName} as {UploadName}", localFile.Name, uploadName);

            // For pngs, strip metadata since Pillow can't handle some valid files?
            if (localFile.Extension.Equals(".png", StringComparison.OrdinalIgnoreCase))
            {
                var bytes = PngDataHelper.RemoveMetadata(
                    await localFile.ReadAllBytesAsync(cancellationToken)
                );
                using var stream = new MemoryStream(bytes);

                await Client.UploadImageAsync(stream, uploadName, cancellationToken);
            }
            else
            {
                await using var stream = localFile.Info.OpenRead();

                await Client.UploadImageAsync(stream, uploadName, cancellationToken);
            }
        }
        else
        {
            logger.LogDebug("Uploading bitmap as {UploadName}", uploadName);

            if (await image.GetBitmapAsync() is not { } bitmap)
            {
                throw new InvalidOperationException("Failed to get bitmap from image");
            }

            await using var ms = new MemoryStream();
            bitmap.Save(ms);
            ms.Position = 0;

            await Client.UploadImageAsync(ms, uploadName, cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task CopyImageToInputAsync(FilePath imageFile, CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
            return;

        if (Client.InputImagesDir is not { } inputImagesDir)
        {
            throw new InvalidOperationException("InputImagesDir is null");
        }

        var inferenceInputs = inputImagesDir.JoinDir("Inference");
        inferenceInputs.Create();

        var destination = inferenceInputs.JoinFile(imageFile.Name);

        // Read to SKImage then write to file, to prevent errors from metadata
        await Task.Run(
            () =>
            {
                using var imageStream = imageFile.Info.OpenRead();
                using var image = SKImage.FromEncodedData(imageStream);
                using var destinationStream = destination.Info.OpenWrite();
                image.Encode(SKEncodedImageFormat.Png, 100).SaveTo(destinationStream);
            },
            cancellationToken
        );
    }

    /// <inheritdoc />
    public async Task WriteImageToInputAsync(
        ImageSource imageSource,
        CancellationToken cancellationToken = default
    )
    {
        if (!IsConnected)
            return;

        if (Client.InputImagesDir is not { } inputImagesDir)
        {
            throw new InvalidOperationException("InputImagesDir is null");
        }

        var inferenceInputs = inputImagesDir.JoinDir("Inference");
        inferenceInputs.Create();
    }

    [MemberNotNull(nameof(Client))]
    private async Task ConnectAsyncImpl(Uri uri, CancellationToken cancellationToken = default)
    {
        if (IsConnected)
            return;

        IsConnecting = true;
        try
        {
            logger.LogDebug("Connecting to {@Uri}...", uri);

            // Warn if HTTP is used with a domain that looks like it should be HTTPS
            if (
                uri.Scheme == "http"
                && !uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                && !uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
            )
            {
                logger.LogWarning(
                    "Using HTTP with remote domain '{Host}'. Cloudflare tunnels and most remote servers require HTTPS. "
                        + "Consider using 'https://{Host}' instead.",
                    uri.Host,
                    uri.Host
                );
            }

            // Create ComfyServerSettings from user settings
            var serverSettings = CreateComfyServerSettingsFromUserSettings();

            var tempClient = new ComfyClient(apiFactory, uri, Options.Create(serverSettings));

            await tempClient.ConnectAsync(cancellationToken);
            logger.LogDebug("Connected to {@Uri}", uri);

            Client = tempClient;

            await LoadSharedPropertiesAsync();
        }
        catch (Refit.ApiException apiEx)
        {
            Client = null;

            // Check if the response is HTML instead of JSON (common with Cloudflare errors)
            if (apiEx.Content is { } content && content.TrimStart().StartsWith("<", StringComparison.Ordinal))
            {
                logger.LogError(
                    apiEx,
                    "Received HTML response instead of JSON from {Uri}. "
                        + "This usually means the server is returning an error page. "
                        + "For Cloudflare tunnels, ensure you're using HTTPS and have proper authentication headers configured.",
                    uri
                );
                throw new InvalidOperationException(
                    $"Server returned HTML instead of JSON. This usually indicates:\n"
                        + $"1. The URL should use HTTPS instead of HTTP (e.g., https://{uri.Host})\n"
                        + $"2. Authentication headers may be missing or incorrect\n"
                        + $"3. The server may be blocking the request\n\n"
                        + $"Original error: {apiEx.Message}",
                    apiEx
                );
            }

            throw;
        }
        catch (Exception ex)
        {
            Client = null;

            // Check for WebSocket connection errors
            if (ex.Message.Contains("status code '200'") && ex.Message.Contains("status code '101'"))
            {
                logger.LogError(
                    ex,
                    "WebSocket connection failed: Server returned HTTP 200 instead of 101 (WebSocket upgrade). "
                        + "For Cloudflare tunnels, ensure you're using HTTPS (wss://) and have proper authentication headers."
                );
                throw new InvalidOperationException(
                    $"WebSocket connection failed. The server returned HTTP 200 instead of upgrading to WebSocket (101).\n\n"
                        + $"This usually means:\n"
                        + $"1. Use HTTPS instead of HTTP (e.g., https://{uri.Host} instead of http://{uri.Host})\n"
                        + $"2. Cloudflare tunnels require HTTPS/WSS connections\n"
                        + $"3. Authentication headers may be required\n\n"
                        + $"Original error: {ex.Message}",
                    ex
                );
            }

            throw;
        }
        finally
        {
            IsConnecting = false;
        }
    }

    /// <summary>
    /// Creates ComfyServerSettings from user settings, parsing auth headers from JSON if provided
    /// </summary>
    private ComfyServerSettings CreateComfyServerSettingsFromUserSettings()
    {
        var settings = new ComfyServerSettings();

        // Parse auth headers from JSON string if provided
        var authHeadersJson = settingsManager.Settings.ComfyUIAuthHeaders;
        if (!string.IsNullOrWhiteSpace(authHeadersJson))
        {
            try
            {
                logger.LogDebug(
                    "Parsing ComfyUI auth headers from JSON: {JsonLength} characters",
                    authHeadersJson.Length
                );
                var headers = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(
                    authHeadersJson
                );
                if (headers != null && headers.Count > 0)
                {
                    // Filter out empty/null values
                    var validHeaders = headers
                        .Where(kvp =>
                            !string.IsNullOrWhiteSpace(kvp.Key) && !string.IsNullOrWhiteSpace(kvp.Value)
                        )
                        .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

                    if (validHeaders.Count > 0)
                    {
                        settings.Headers = validHeaders;
                        logger.LogDebug(
                            "Loaded {Count} valid auth headers from user settings: {HeaderNames}",
                            validHeaders.Count,
                            string.Join(", ", validHeaders.Keys)
                        );

                        // Log header values in trace mode (but mask sensitive values)
                        if (logger.IsEnabled(LogLevel.Trace))
                        {
                            foreach (var header in validHeaders)
                            {
                                var maskedValue =
                                    header.Value.Length > 8
                                        ? header.Value[..4] + "..." + header.Value[^4..]
                                        : "***";
                                logger.LogTrace("Header '{Key}' = '{Value}'", header.Key, maskedValue);
                            }
                        }
                    }
                    else
                    {
                        logger.LogWarning(
                            "Parsed {TotalCount} headers but none were valid (all had empty keys or values)",
                            headers.Count
                        );
                    }
                }
                else
                {
                    logger.LogWarning("Parsed headers dictionary was null or empty");
                }
            }
            catch (System.Text.Json.JsonException ex)
            {
                logger.LogError(
                    ex,
                    "Failed to parse ComfyUI auth headers from user settings. JSON: {JsonPreview}",
                    authHeadersJson.Length > 100 ? authHeadersJson[..100] + "..." : authHeadersJson
                );
            }
        }
        else
        {
            logger.LogDebug("No ComfyUI auth headers configured in user settings");
        }

        return settings;
    }

    private async Task MigrateLinksIfNeeded(PackagePair packagePair)
    {
        if (packagePair.InstalledPackage.FullPath is not { } packagePath)
        {
            throw new ArgumentException("Package path is null", nameof(packagePair));
        }

        var inferenceDir = settingsManager.ImagesInferenceDirectory;
        inferenceDir.Create();

        // For locally installed packages only
        // Delete ./output/Inference

        var legacyInferenceLinkDir = new DirectoryPath(packagePair.InstalledPackage.FullPath).JoinDir(
            "output",
            "Inference"
        );

        if (legacyInferenceLinkDir.Exists)
        {
            logger.LogInformation("Deleting legacy inference link at {LegacyDir}", legacyInferenceLinkDir);

            if (legacyInferenceLinkDir.IsSymbolicLink)
            {
                await legacyInferenceLinkDir.DeleteAsync(false);
            }
            else
            {
                logger.LogWarning(
                    "Legacy inference link at {LegacyDir} is not a symbolic link, skipping",
                    legacyInferenceLinkDir
                );
            }
        }
    }

    /// <inheritdoc />
    public virtual Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        // Use user-configured host/port if set, otherwise default to localhost:8188
        var host = settingsManager.Settings.ComfyUIHost;
        var port = settingsManager.Settings.ComfyUIPort;

        Uri uri;

        // Check if host is a full URL (starts with http:// or https://)
        if (
            !string.IsNullOrWhiteSpace(host)
            && (
                host.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || host.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            )
        )
        {
            // Use the URL directly
            if (Uri.TryCreate(host, UriKind.Absolute, out var parsedUri))
            {
                uri = parsedUri;
            }
            else
            {
                logger.LogWarning("Invalid ComfyUI URL format: {Host}", host);
                // Fall back to default
                uri = new UriBuilder("http", "127.0.0.1", 8188).Uri;
            }
        }
        else
        {
            // Legacy host/port mode or host:port format
            string hostName;
            int portNumber;

            if (string.IsNullOrWhiteSpace(host))
            {
                hostName = "127.0.0.1";
                portNumber = 8188;
            }
            else
            {
                // Check if host contains a port (format: host:port)
                var hostParts = host.Split(':', 2);
                if (hostParts.Length == 2 && int.TryParse(hostParts[1], out portNumber))
                {
                    // Host contains port in format host:port
                    hostName = hostParts[0];
                }
                else
                {
                    // Use separate port field if provided
                    hostName = host;
                    if (string.IsNullOrWhiteSpace(port) || !int.TryParse(port, out portNumber))
                    {
                        portNumber = 8188;
                    }
                }

                hostName = hostName.Replace("localhost", "127.0.0.1");
            }

            uri = new UriBuilder("http", hostName, portNumber).Uri;
        }

        return ConnectAsyncImpl(uri, cancellationToken);
    }

    /// <inheritdoc />
    public virtual async Task ConnectAsync(
        PackagePair packagePair,
        CancellationToken cancellationToken = default
    )
    {
        if (IsConnected)
            return;

        if (packagePair.BasePackage is not ComfyUI comfyPackage)
        {
            throw new ArgumentException("Base package is not ComfyUI", nameof(packagePair));
        }

        // Setup completion provider
        completionProvider
            .Setup()
            .SafeFireAndForget(ex =>
            {
                logger.LogError(ex, "Error setting up completion provider");
            });

        await MigrateLinksIfNeeded(packagePair);

        // Get user defined host and port
        var host = packagePair.InstalledPackage.GetLaunchArgsHost();
        if (string.IsNullOrWhiteSpace(host))
        {
            host = "127.0.0.1";
        }
        host = host.Replace("localhost", "127.0.0.1");

        var port = packagePair.InstalledPackage.GetLaunchArgsPort();
        if (string.IsNullOrWhiteSpace(port))
        {
            port = "8188";
        }

        var uri = new UriBuilder("http", host, int.Parse(port)).Uri;

        await ConnectAsyncImpl(uri, cancellationToken);

        Client.LocalServerPackage = packagePair;
        Client.LocalServerPath = packagePair.InstalledPackage.FullPath!;
    }

    public async Task CloseAsync()
    {
        if (!IsConnected)
            return;

        await Client.CloseAsync();
        Client = null;
        ResetSharedProperties();
    }

    public void Dispose()
    {
        Client?.Dispose();
        Client = null;
        GC.SuppressFinalize(this);
    }

    ~InferenceClientManager()
    {
        Dispose();
    }
}
