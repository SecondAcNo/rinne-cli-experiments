using Microsoft.Extensions.DependencyInjection;
using Rinne.Cli.Commands;
using Rinne.Cli.Interfaces.Commands;
using Rinne.Cli.Interfaces.Services;
using Rinne.Cli.Interfaces.System;
using Rinne.Cli.Interfaces.Utility;
using Rinne.Cli.Models;
using Rinne.Cli.Services;
using Rinne.Cli.System;
using Rinne.Cli.Utility;

namespace Rinne.Cli.DI
{
    /// <summary>
    /// Rinne.Cli 層で使用するサービス群を依存性注入 (DI) コンテナに登録する拡張メソッド。
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// CLI 層固有のサービス群を登録します。
        /// </summary>
        /// <param name="services"><see cref="IServiceCollection"/> のインスタンス。</param>
        /// <returns>登録後の <see cref="IServiceCollection"/>。</returns>
        public static IServiceCollection AddCliServices(this IServiceCollection services)
        {
            // Service
            services.AddSingleton<IArchiveService, ArchiveService>();
            services.AddSingleton<IMetaService, MetaService>();
            services.AddSingleton<IMetaVerifyService, MetaVerifyService>();
            services.AddScoped<IRestoreService, RestoreService>();
            services.AddSingleton<ISaveService, SaveService>();
            services.AddSingleton<IArchiveDiffService, ArchiveDiffService>();
            services.AddSingleton<IInitService, InitService>();
            services.AddSingleton<ILogService, LogService>();
            services.AddSingleton<ISpaceService, SpaceService>();
            services.AddSingleton<IShowService, ShowService>();
            services.AddSingleton<ITextDiffService , TextDiffService>();
            services.AddSingleton<IBackupService, BackupService>();
            services.AddSingleton<ISpaceImportService , SpaceImportService>();
            services.AddSingleton<IDropLastService, DropLastService>();
            services.AddSingleton<ITidyService, TidyService>();

            // Command
            services.AddSingleton<ICliCommand, SaveCommand>();
            services.AddSingleton<ICliCommand, InitCommand>();
            services.AddSingleton<ICliCommand, VerifyCommand>();
            services.AddSingleton<ICliCommand, RestoreCommand>();
            services.AddSingleton<ICliCommand, SpaceCommand>();
            services.AddSingleton<ICliCommand, DiffCommand>();
            services.AddSingleton<ICliCommand, TextDiffCommand>();
            services.AddSingleton<ICliCommand, LogCommand>();
            services.AddSingleton<ICliCommand, ShowCommand>();
            services.AddSingleton<ICliCommand, RecomposeCommand>();
            services.AddSingleton<ICliCommand, BackupCommand>();
            services.AddSingleton<ICliCommand, ImportCommand>();
            services.AddSingleton<ICliCommand, DropLastCommand>();
            services.AddSingleton<ICliCommand, LogOutputCommand>();
            services.AddSingleton<ICliCommand, TidyCommand>();
            services.AddSingleton<ICliCommand, VersionCommand>();

            // Utility
            services.AddSingleton<IGlobMatcherFactory, GlobMatcherFactory>();
            services.AddSingleton<IConsoleDiffFormatter, ConsoleDiffFormatter>();

            // System
            services.AddSingleton<IDirectoryCleaner, DirectoryCleaner>();

            return services;
        }
    }
}
