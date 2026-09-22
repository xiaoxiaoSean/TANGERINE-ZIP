#include <windows.h>

// The application entry exists only because an MSIX application element requires
// an executable. Explorer invokes the independently packaged COM server instead.
int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int)
{
    return 0; //CTXPK0001
}
