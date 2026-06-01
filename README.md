# WinFolderLock

## Problem

Windows makes folder privacy more annoying than it needs to be. If you want to keep a folder away from casual snooping, the “proper” Windows way usually involves setting up multiple user accounts and messing with permissions.

## Overview
A simple Windows utility that protects folders using a custom `.wflck` extension. WinFolderLock integrates directly with the Windows context menu, so you can lock and unlock folders with a quick right-click.

> **Note:** This program is intended for **convenience** rather than strict, enterprise-style security. It gives you an easy way to password-protect folders from casual access without having to set up multiple Windows users.

**Use at your own volition**, and back up important files before locking them away. I haven't run into any issues while testing, but that doesn't mean you won't.

**Made with the help of GitHub Copilot.**

## Installation & Setup

> **Administrator privileges are required** to install and uninstall this program.

Extract the folder and run `WinFolderLock.exe` to open the installation wizard.

> Install/Repair: This registers the required context menu entries and places the application files in `C:\Program Files\WinFolderLock`.
>
> Uninstall: It will prompt you to uninstall the program, permanently unlock any locked folders, and clean up after itself.

## Basic Usage

### Locking a Folder

1. Right-click the folder you want to protect.
2. Select **Lock Folder**.
3. You will be prompted to create or enter a password.
4. Once completed, your folder is compressed, encrypted, and replaced with a locked folder (`.wflck` file).

### Temporarily Unlocking a Folder

1. Double-click the locked folder (`.wflck` file), or right-click it and select **Unlock Folder**.
2. Enter the password you used to lock the folder.
3. WinFolderLock creates a temporary session folder and opens it in Windows Explorer so you can work on the files.
> **Important**: Once you close the Windows Explorer window(s) associated with that folder, WinFolderLock automatically saves any saved changes inside the folder, re-encrypts the folder, and removes the temporary access.

### Permanently Unlocking a Folder

1. Right-click the locked folder (`.wflck` file) and select **Permanent Unlock**.
2. Enter the correct password.
3. The original folder is fully restored, and the locked folder (`.wflck` file) is deleted.

*Note: Passwords mapped to locked folders are kept track of securely per local session at `C:\ProgramData\WinFolderLock\passwords.json`.*
