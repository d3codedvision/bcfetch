using BcFetch.Audio;
using BcFetch.Bandcamp.Download;
using BcFetch.Bandcamp.Extraction;
using BcFetch.Core.Logging;
using BcFetch.Core.Updates;
using BcFetch.Helpers;
using BcFetch.IO;
using BcFetch.Net;
using BcFetch.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace BcFetch.Core.DependencyInjection;

/// <summary>
/// Wrapper around the underlying dependency injection container.
/// </summary>
internal interface IContainer
{
    T GetService<T>();
}

internal sealed class Container : IContainer
{
    private readonly ServiceProvider _serviceProvider;

    public Container()
    {
        var serviceCollection = GetServiceCollection();
        _serviceProvider = serviceCollection.BuildServiceProvider();
    }

    public T GetService<T>()
    {
        return _serviceProvider.GetRequiredService<T>();
    }

    private static ServiceCollection GetServiceCollection()
    {
        var serviceCollection = new ServiceCollection();

        serviceCollection.AddHttpClient();
        serviceCollection.AddSingleton<IAlbumInfoRetriever, AlbumInfoRetriever>();
        serviceCollection.AddSingleton<IAlbumInfoParser, AlbumInfoParser>();
        serviceCollection.AddSingleton<IAlbumUrlRetriever, AlbumUrlRetriever>();
        serviceCollection.AddSingleton<IDiscographyService, DiscographyService>();
        serviceCollection.AddSingleton<IDownloadManager, DownloadManager>();
        serviceCollection.AddSingleton<IFileService, FileService>();
        serviceCollection.AddSingleton<IHttpService, HttpService>();
        serviceCollection.AddSingleton<IImageService, ImageService>();
        serviceCollection.AddSingleton<ILoggingService, LoggingService>();
        serviceCollection.AddSingleton<IPlaylistCreator, PlaylistCreator>();
        serviceCollection.AddSingleton<IResilienceService, ResilienceService>();
        serviceCollection.AddSingleton<ISettingsService, SettingsService>();
        serviceCollection.AddSingleton<ITagService, TagService>();
        serviceCollection.AddSingleton<ITrackFileService, TrackFileService>();
        serviceCollection.AddSingleton<IUpdatesService, UpdatesService>();

        return serviceCollection;
    }
}
