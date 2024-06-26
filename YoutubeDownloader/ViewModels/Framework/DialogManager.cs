﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using DialogHostAvalonia;
using YoutubeDownloader.Views.Framework;

namespace YoutubeDownloader.ViewModels.Framework;

public class DialogManager(IViewManager viewManager) : IDisposable
{
    private readonly SemaphoreSlim _dialogLock = new(1, 1);

    public async ValueTask<T?> ShowDialogAsync<T>(DialogScreen<T> dialogScreen)
    {
        var view = viewManager.CreateAndBindViewForModelIfNecessary(dialogScreen);

        void OnDialogOpened(object? openSender, DialogOpenedEventArgs openArgs)
        {
            void OnScreenClosed(object? closeSender, EventArgs closeArgs)
            {
                try
                {
                    openArgs.Session.Close();
                }
                catch (InvalidOperationException)
                {
                    // Race condition: dialog is already being closed
                }

                dialogScreen.Closed -= OnScreenClosed;
            }

            dialogScreen.Closed += OnScreenClosed;
        }

        await _dialogLock.WaitAsync();
        try
        {
            await DialogHost.Show(view!, OnDialogOpened);
            return dialogScreen.DialogResult;
        }
        finally
        {
            _dialogLock.Release();
        }
    }

    public async Task<string?> PromptSaveFilePathAsync(
        IReadOnlyList<FilePickerFileType>? fileTypes = null,
        string defaultFilePath = ""
    )
    {
        var topLevel = viewManager.GetTopLevel();

        var storageProvider = topLevel?.StorageProvider;
        if (storageProvider is null)
        {
            return null;
        }

        var filePickResult = await storageProvider.SaveFilePickerAsync(
            new()
            {
                FileTypeChoices = fileTypes,
                SuggestedFileName = defaultFilePath,
                DefaultExtension = Path.GetExtension(defaultFilePath).TrimStart('.')
            }
        );

        if (filePickResult?.Path is Uri path)
        {
            return path.LocalPath;
        }

        return null;
    }

    public async Task<string?> PromptDirectoryPathAsync(string defaultDirPath = "")
    {
        var topLevel = viewManager.GetTopLevel();

        var storageProvider = topLevel?.StorageProvider;
        if (storageProvider is null)
        {
            return null;
        }

        var startLocation = await GetStorageFolderAsync(storageProvider, defaultDirPath);
        var folderPickResult = await storageProvider.OpenFolderPickerAsync(
            new() { AllowMultiple = false, SuggestedStartLocation = startLocation }
        );

        if (folderPickResult.FirstOrDefault()?.Path is Uri path)
        {
            return path.LocalPath;
        }

        return null;
    }

    public void Dispose()
    {
        _dialogLock.Dispose();
    }

    private static async Task<IStorageFolder?> GetStorageFolderAsync(
        IStorageProvider storageProvider,
        string path
    )
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        var storageFolder = await storageProvider.TryGetFolderFromPathAsync(path);

        return storageFolder;
    }
}
