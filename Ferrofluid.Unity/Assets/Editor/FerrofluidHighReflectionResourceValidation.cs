using System.Collections;

namespace ChromaGLS.FerrofluidBundle
{
    internal static class FerrofluidHighReflectionResourceValidation
    {
        public static void Run() => FerrofluidValidationRunner.Run(typeof(FerrofluidHighReflectionResourceValidation));

        private static IEnumerator RunRuntime() => FerrofluidReflectionResourceValidation.Validate(true);
    }
}
