# FileSync Pro - File Structure

This document provides an overview of all files created for the FileSync Pro application.

## Project Files
- FileSyncPro.sln - Visual Studio solution file
- FileSyncPro.csproj - C# project file with dependencies

## Models
- Models/Connections.cs - Contains connection models (SftpConnection, SharePointConnection, LocalPathConnection), TransferLog, TransferStatus, ValidationResult

## Services
- Services/ConnectionValidator.cs - Validates SFTP, SharePoint, and local connections
- Services/TransferManager.cs - Manages the complete transfer workflow including download, unzip, and copy operations
- Services/IntegrityChecker.cs - Verifies file integrity after transfers using size and checksum comparisons
- Services/LogManager.cs - Handles logging and audit trail functionality

## ViewModels
- ViewModels/MainViewModel.cs - Main application logic, commands, and data binding

## Views
- Views/MainWindow.xaml - Main application UI with tabbed interface
- Views/MainWindow.xaml.cs - Code-behind for the main window

## Utilities
- Utilities/BaseViewModel.cs - Base class for ViewModels with property change notification
- Utilities/RelayCommand.cs - Implementation of ICommand interface
- Utilities/PasswordHelper.cs - Attached property for binding PasswordBox controls

## Converters
- Converters/Converters.cs - Value converters for XAML binding (IntToBoolConverter, InverseBooleanConverter, ConnectionTypeToStringConverter, etc.)

## Other Files
- App.xaml - Application entry point markup
- App.xaml.cs - Application entry point code
- README.md - Documentation for the application