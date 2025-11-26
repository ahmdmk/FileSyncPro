# FileSync Pro

FileSync Pro is a professional, reliable Windows desktop application for synchronizing and copying files between SFTP servers, SharePoint locations, and local folders. The application focuses on providing a secure, validated, and user-friendly file transfer experience with integrity checks and complete logging.

## Features

- **Multiple Connection Types**: Connect to SFTP servers, SharePoint sites, or local folders
- **Secure Transfer**: Validates connections before transferring files
- **Unzip & Copy Workflow**: Automatically downloads, unzips, and transfers files
- **Integrity Checks**: Verifies file integrity after transfer using size and checksum comparisons
- **Comprehensive Logging**: Detailed audit trail of all operations
- **User-Friendly UI**: Clean, professional interface with real-time progress updates

## System Requirements

- Windows 10 or later
- .NET 6.0 Runtime or later
- Network access to target SFTP/SharePoint systems
- Appropriate credentials for target systems

## Setup Instructions

1. Clone or download this repository
2. Open the solution file `FileSyncPro.sln` in Visual Studio 2022 or later
3. Restore NuGet packages:
   - In Visual Studio: Build → Restore NuGet Packages
   - Or via command line: `dotnet restore`
4. Build the solution: `dotnet build` or press Ctrl+Shift+B in Visual Studio
5. Run the application: `dotnet run` or press F5 in Visual Studio

### Required NuGet Packages

The application requires the following NuGet packages:
- Renci.SshNet (for SFTP operations)
- Microsoft.SharePointOnline.CSOM (for SharePoint operations)  
- NLog (for logging capabilities)
- System.IO.Compression (for ZIP handling)

## How to Use

1. **Configure Source**: 
   - Select the source type (SFTP, SharePoint, or Local Folder)
   - Enter the required connection details
   - Click "Validate Source" to test the connection

2. **Configure Destination**:
   - Select the destination type (SFTP, SharePoint, or Local Folder)
   - Enter the required connection details
   - Click "Validate Destination" to test the connection

3. **Start Transfer**:
   - Enter the name of the file to transfer
   - Click "Start Transfer" to begin the operation
   - Monitor progress in the Transfer Progress tab

4. **View Logs**:
   - Check the Transfer Logs tab for operation history
   - Use "Clear Logs" or "Export Logs" as needed

## Security Considerations

- Store credentials securely; this application stores them only in memory during the session
- Verify all connection details before initiating transfers
- Review logs regularly to audit file transfer activity

## Troubleshooting

### Common Issues:

1. **NuGet Restore Errors**:
   - Check your internet connection
   - Verify access to nuget.org
   - Try clearing the NuGet cache: `dotnet nuget locals all --clear`

2. **Connection Failures**:
   - Verify hostnames, IP addresses, or URLs
   - Confirm credentials are correct
   - Check firewall settings for required ports (typically 22 for SFTP, 443 for SharePoint)

3. **Transfer Failures**:
   - Ensure sufficient storage space at the destination
   - Verify write permissions at the destination
   - Check network connectivity during transfer

## Architecture

The application follows the MVVM (Model-View-ViewModel) pattern:

- **Models**: Data structures for connections and logs
- **Views**: WPF XAML interfaces
- **ViewModels**: Business logic and command implementation
- **Services**: Connection validation, transfer management, integrity checking, and logging

## License

This application is provided as a reference implementation. Modify and use according to your organization's policies.