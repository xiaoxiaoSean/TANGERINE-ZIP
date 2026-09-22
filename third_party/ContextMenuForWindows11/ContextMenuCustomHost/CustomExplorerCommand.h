#pragma once
#include "BaseExplorerCommand.h"
#include "CustomSubExplorerCommand.h"
#include <string>
// TZIP uses a dedicated CLSID so it cannot collide with any installed edition
// of Custom Context Menu. The implementation remains independently replaceable
// under LGPL-3.0 as documented in third_party/ContextMenuForWindows11/LICENSE.
class __declspec(uuid("FFCE1E90-8F9C-4F49-A9B6-C35A69B30AE9"))
CustomExplorerCommand: public BaseExplorerCommand{
public:
	CustomExplorerCommand();
	IFACEMETHODIMP GetFlags(_Out_ EXPCMDFLAGS* flags) override;
	IFACEMETHODIMP GetState(_In_opt_ IShellItemArray* selection, _In_ BOOL okToBeSlow, _Out_ EXPCMDSTATE* cmdState) override;
	IFACEMETHODIMP GetTitle(_In_opt_ IShellItemArray* items, _Outptr_result_nullonfailure_ PWSTR* name) override;
	IFACEMETHODIMP GetIcon(_In_opt_ IShellItemArray*, _Outptr_result_nullonfailure_ PWSTR* icon) override;
	IFACEMETHODIMP GetCanonicalName(_Out_ GUID* guidCommandName) override;
	IFACEMETHODIMP EnumSubCommands(__RPC__deref_out_opt IEnumExplorerCommand** enumCommands) override;
	IFACEMETHODIMP Invoke(_In_opt_ IShellItemArray* selection, _In_opt_ IBindCtx*) noexcept override;
	void ReadCommands(IShellItemArray* selection, bool multipleFiles, bool isDirectory, bool isBackground, bool isDesktop, const std::wstring& currentPath);
	HRESULT FindLocationFromSite(IShellItem** location) const noexcept;

private:
	std::vector<ComPtr<CustomSubExplorerCommand>> m_commands;

};
