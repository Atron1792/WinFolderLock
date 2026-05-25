# WinFolderLock

> **Note:** This program is intended for **convenience** rather than strict, enterprise-style security. It provides an easy way to password-protect folders from casual access without having to set up multiple users on Windows.

A simple utility to protect your folders on Windows using a custom `.wflck` extension. WinFolderLock integrates directly with the Windows context menu, allowing you to easily lock and unlock folders.

## Overview
WinFolderLock compresses and encrypts target folders into `.wflck` files. This tool ensures that without the proper password, the contents are kept hidden from casual snooping. The key used for encryption is additionally protected by the built-in Windows Data Protection API (DPAPI) for the current user.

**Use at your own volition**, backup important files before locking them away. I haven't run into any problems while testing, but that doesn't mean you won't. 

**Made with the help of GitHub Copilot.**

## Features
- Context menu integration: Right-click on a folder to lock it or on a `.wflck` file to unlock it.
- Temporary Unlocking allows temporary access to a folder, automatically re-locking once you're done.
- Permanent Unlock restores the folder to its regular unencrypted state.

## Installation & Setup
> **Administrator privileges are required** to install and uninstall this program.

Extract the folder and run the `WinFolderLock.exe` to open the installation wizard.
Follow the steps in the application wizard to install. This registers the required context menu entries and places application files into `C:\ProgramData\WinFolderLock`.

To uninstall, run the wizard again; it will prompt you for the uninstallation and permanently unlock all locked folders properly, cleaning up after itself.

## Basic Usage

### Locking a Folder
1. Right-click on the folder you wish to protect.
2. Select **Lock Folder**.
3. You will be prompted to create or enter a password.
4. Once completed, your folder is compressed, encrypted, and replaced with a `.wflck` file.

### Temporarily Unlocking a Folder
If you quickly need to access your files:
1. Double-click or Right-click on the `.wflck` file and select **Unlock Folder**.
2. Enter the password you used to lock the folder.
3. WinFolderLock creates a temporary session folder and opens it in Windows Explorer so you can work on the files.
4. **Important**: Once you close the Windows Explorer window(s) associated with that folder, WinFolderLock automatically saves any changes, re-encrypts the folder, and replaces the temporary access.

### Permanently Unlocking a Folder
If you no longer need protection on a folder:
1. Right-click on the `.wflck` file and select **Permanent Unlock**.
2. Enter the correct password.
3. The original folder is fully restored, and the `.wflck` file is deleted.

*Note: Passwords mapped to locked folders are kept track of securely per local session at `C:\ProgramData\WinFolderLock\passwords.json`.* 
