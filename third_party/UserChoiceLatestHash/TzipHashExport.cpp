#include "HashCommon.h"

// Keep the native boundary deliberately small. The WPF host supplies the
// canonical Windows association input and verifies the effective association.
// This function never reads or writes the registry and cannot switch defaults.
extern "C" __declspec(dllexport) int __stdcall TzipComputeLatestHash(
    const wchar_t* canonical_input, wchar_t* output, unsigned int output_chars) noexcept
{
    if (canonical_input == nullptr || output == nullptr || output_chars < 16U ||
        wcsnlen_s(canonical_input, 4097U) > 4096U)
    {
        return 0;
    }

    try
    {
        UserChoiceLatestHash::WorkingSeeds seeds;
        UserChoiceLatestHash::LoadProvidedSeeds(&seeds);
        std::wstring hash;
        if (!UserChoiceLatestHash::ComputeHash(canonical_input, seeds, false, &hash, nullptr) ||
            hash.size() + 1U > output_chars)
        {
            return 0;
        }
        wcscpy_s(output, output_chars, hash.c_str());
        return 1;
    }
    catch (...)
    {
        return 0;
    }
}
