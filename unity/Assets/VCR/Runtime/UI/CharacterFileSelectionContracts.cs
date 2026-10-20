using System;

namespace VCR.Runtime.UI
{
    public readonly struct CharacterFileSelectionResult
    {
        public CharacterFileSelectionResult(
            bool selected,
            bool cancelled,
            string path,
            string error)
        {
            Selected = selected;
            Cancelled = cancelled;
            Path = path;
            Error = error;
        }

        public bool Selected { get; }
        public bool Cancelled { get; }
        public string Path { get; }
        public string Error { get; }

        public static CharacterFileSelectionResult
            Success(
                string path) =>
                new(
                    true,
                    false,
                    path,
                    null);

        public static CharacterFileSelectionResult
            CancelledResult() =>
                new(
                    false,
                    true,
                    null,
                    null);

        public static CharacterFileSelectionResult
            Failure(
                string error) =>
                new(
                    false,
                    false,
                    null,
                    error);
    }

    public interface ICharacterFileSelectionAdapter
    {
        bool IsSupported { get; }
        string AdapterId { get; }
        string UnavailableReason { get; }

        CharacterFileSelectionResult
            SelectCharacterFile(
                string currentPath);
    }
}
