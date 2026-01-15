#include <windows.h>
#include <iostream>
#include <string>

typedef int (*StartTcpServerFunc)(int port, const char* type);
typedef int (*ProcessDataFunc)(const char* ip, const char* username, const char* password, const char* isapi, const char* type, char* resultBuffer, int resultBufferSize);
typedef int (*GetLastResultFunc)(int port,char* buffer, int bufferSize);
typedef int (*StopTcpServerFunc)(int port);

int main() {
    // 1. Load DLL
    HMODULE dll = LoadLibraryA("HIKVisionDLL.dll");
    if (!dll) {
        std::cout << "Failed to load DLL! Make sure HIKVisionDLL.dll is in the same folder.\n";
        return 1;
    }

    // 2. Get function address
    auto processData = (ProcessDataFunc)GetProcAddress(dll, "ProcessData");
    auto startServer = (StartTcpServerFunc)GetProcAddress(dll, "StartTcpServer");
    auto getLastResult = (GetLastResultFunc)GetProcAddress(dll, "GetLastResult");
    auto stopServer = (StopTcpServerFunc)GetProcAddress(dll, "StopTcpServer");
    
    char buffer[1024];

    // 3. Manual Testing
    std::cout << "\n--- Manual Capture Testing ---" << std::endl;
    
    memset(buffer, 0, 1024);
    int lenLPR = processData("192.168.1.65", "admin", "abcd2468", "", "LPR", buffer, 1024);
    if (lenLPR > 0) std::cout << "Manual Capture LPR Result: " << buffer << std::endl;
    else std::cout << "Manual Capture LPR: No data or connection failed." << std::endl;

    memset(buffer, 0, 1024);
    int lenCNR = processData("192.168.1.64", "admin", "abcd2468", "", "CNR", buffer, 1024);
    if (lenCNR > 0) std::cout << "Manual Capture CNR Result: " << buffer << std::endl;
    else std::cout << "Manual Capture CNR: No data or connection failed." << std::endl;

    // 4. Start the server
    std::cout << "\nStarting LPR (8088) and CNR (8087)...\n";
    int res1 = startServer(8088, "LPR");
    int res2 = startServer(8087, "CNR");

    if (res1 == 1 && res2 == 1) {
        std::cout << "Both servers started successfully.\n";
    } else {
        std::cout << "\n[ERROR] One or more servers failed to start!" << std::endl;
        std::cout << "Check if ports 8087/8088 are already in use by another instance." << std::endl;
        
        FreeLibrary(dll);
        std::cout << "Program will exit now." << std::endl;
        system("pause > nul");
        return 1;
    }

    // 5. Enter real-time monitoring loop
    std::cout << "\n--- Motion Detect Testing (Press Ctrl+C to stop) ---\n";

    // Continuously poll for new camera push data
    while (true) {
        memset(buffer, 0, 1024);
        int len8088 = getLastResult(8088, buffer, 1024);
        if (len8088 > 0) {
            std::cout << "[LPR 8088] " << buffer << std::endl;
        }

        memset(buffer, 0, 1024);
        int len8087 = getLastResult(8087, buffer, 1024);
        if (len8087 > 0) {
            std::cout << "[CNR 8087] " << buffer << std::endl;
        }

        // Check every 500 milliseconds to avoid excessive CPU usage.
        Sleep(500);
    }

    // Normally, this will not be executed unless the while condition is modified.
    stopServer(8088);
    stopServer(8087);
    FreeLibrary(dll);
    return 0;
}