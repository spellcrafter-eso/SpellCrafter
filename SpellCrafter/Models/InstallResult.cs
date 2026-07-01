using System;

namespace SpellCrafter.Models
{
    public sealed record InstallResult
    {
        public bool Succeeded { get; init; }
        public bool WasCanceled { get; init; }
        public bool CompletedAfterCancellation { get; init; }
        public string? ErrorMessage { get; init; }
        public string? WarningMessage { get; init; }
        public Exception? Exception { get; init; }
        public string? BackupPath { get; init; }

        public static InstallResult Success(
            string? warningMessage = null,
            bool completedAfterCancellation = false) => new()
        {
            Succeeded = true,
            WarningMessage = warningMessage,
            CompletedAfterCancellation = completedAfterCancellation
        };

        public static InstallResult Canceled(
            string errorMessage,
            Exception? exception = null,
            string? backupPath = null) => new()
        {
            Succeeded = false,
            WasCanceled = true,
            ErrorMessage = errorMessage,
            Exception = exception,
            BackupPath = backupPath
        };

        public static InstallResult Failure(
            string errorMessage,
            Exception? exception = null,
            string? backupPath = null) => new()
        {
            Succeeded = false,
            ErrorMessage = errorMessage,
            Exception = exception,
            BackupPath = backupPath
        };
    }
}
