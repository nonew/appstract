﻿#region Copyright (C) 2008-2009 Simon Allaeys

/*
    Copyright (C) 2008-2009 Simon Allaeys
 
    This file is part of AppStract

    AppStract is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    AppStract is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with AppStract.  If not, see <http://www.gnu.org/licenses/>.
*/

#endregion

using System;
using System.Runtime.InteropServices;
using AppStract.Core.System.Logging;
using AppStract.Core.Virtualization.Registry;
using AppStract.Utilities.Extensions;
using AppStract.Utilities.Interop;
using Microsoft.Win32.Interop;
using ValueType = AppStract.Core.Virtualization.Registry.ValueType;

namespace AppStract.Server.Hooking
{
  public partial class HookImplementations
  {

    #region Public Methods

    /// <summary>
    /// Open key will open a key  from the VREG, or create it if not available.
    /// </summary>
    /// <param name="hKey"></param>
    /// <param name="subKey"></param>
    /// <param name="options"></param>
    /// <param name="sam"></param>
    /// <param name="phkResult"></param>
    /// <returns></returns>
    public uint RegOpenKey_Hooked(UIntPtr hKey, string subKey, RegOption options, RegSecurityDescriptor sam, out UIntPtr phkResult)
    {
      if (subKey == null)
      {
        phkResult = hKey;
        return WinError.ERROR_SUCCESS;
      }
      uint handle;
      if (!TryParse(hKey, out handle))
      {
        phkResult = UIntPtr.Zero;
        return WinError.ERROR_INVALID_HANDLE;
      }
      using (HookManager.ACL.GetHookingExclusion())
      {
        uint hSubKey;
        var winError = _registry.OpenKey(handle, subKey, out hSubKey);
        GuestCore.Log(new LogMessage(LogLevel.Debug, @"OpenKey({0}\\{1}) => {2}", hKey, subKey,
                                     winError == NativeResultCode.Succes ? hSubKey.ToString() : winError.ToString()));
        phkResult = new UIntPtr(hSubKey);
        return WinError.FromStateCode(winError);
      }
    }

    /// <summary>
    /// Creates the specified registry key. If the key already exists, the function opens it.
    /// </summary>
    /// <param name="hKey">A handle to an open registry key.</param>
    /// <param name="lpSubKey">
    /// The name of a subkey that this function opens or creates. The subkey specified must be
    /// a subkey of the key identified by the hKey parameter. The parameter cannot be NULL.
    /// </param>
    /// <param name="Reserved">This parameter is reserved and must be zero.</param>
    /// <param name="lpClass">Ignored.</param>
    /// <param name="dwOptions">Ignored.</param>
    /// <param name="samDesired">Ignored.</param>
    /// <param name="lpSecurityAttributes">Ignored.</param>
    /// <param name="phkResult">
    /// A pointer to a variable that receives a handle to the opened or created key.
    /// If the key is not one of the predefined registry keys, call the RegCloseKey function
    /// after you have finished using the handle.
    /// </param>
    /// <param name="lpdwDisposition">
    /// A pointer to a variable that receives 0x00000001L if the key is created,
    /// or 0x00000002L if the key is opened.
    /// </param>
    /// <returns></returns>
    public uint RegCreateKeyEx_Hooked(UIntPtr hKey, string lpSubKey, int Reserved, string lpClass, RegOption dwOptions,
      RegSecurityDescriptor samDesired, ref int lpSecurityAttributes, out UIntPtr phkResult, out RegCreationDisposition lpdwDisposition)
    {
      if (lpSubKey == null)
      {
        phkResult = UIntPtr.Zero; // Bug: In this case, Windows doesn't set a value for phkResult! Should phkResult be a "ref" in stead of "out"?
        lpdwDisposition = RegCreationDisposition.NoKeyCreated;
        return WinError.ERROR_BADKEY;
      }
      uint handle;
      if (!TryParse(hKey, out handle))
      {
        phkResult = UIntPtr.Zero;
        lpdwDisposition = RegCreationDisposition.NoKeyCreated;
        return WinError.ERROR_INVALID_HANDLE;
      }
      using (HookManager.ACL.GetHookingExclusion())
      {
        uint phkResultHandle;
        var stateCode = _registry.CreateKey(handle, lpSubKey, out phkResultHandle, out lpdwDisposition);
        GuestCore.Log(new LogMessage(LogLevel.Debug, "CreateKey(HKey={0} NewSubKey={1}) => {2} HKey={3}",
                                     hKey, lpSubKey, lpdwDisposition, phkResultHandle));
        phkResult = new UIntPtr(phkResultHandle);
        return WinError.FromStateCode(stateCode);
      }
    }

    /// <summary>
    /// Closes a key.
    /// </summary>
    /// <param name="hKey"></param>
    /// <returns></returns>
    public uint RegCloseKey_Hooked(UIntPtr hKey)
    {
      uint handle;
      if (!TryParse(hKey, out handle))
        return WinError.ERROR_INVALID_HANDLE;
      using (HookManager.ACL.GetHookingExclusion())
      {
        GuestCore.Log(new LogMessage(LogLevel.Debug, "CloseKey(HKey={0})", handle));
        var stateCode = _registry.CloseKey(handle);
        return WinError.FromStateCode(stateCode);
      }
    }

    /// <summary>
    /// Queries a key from the VREG, or if not exists from the RREG.
    /// </summary>
    /// <remarks>
    /// Documentation from MSDN:
    /// If the function succeeds, the return value is ERROR_SUCCESS.
    /// If the lpData buffer is too small to receive the data, the function returns ERROR_MORE_DATA.
    /// If the lpValueName registry value does not exist, the function returns ERROR_FILE_NOT_FOUND.
    /// </remarks>
    /// <param name="hKey"></param>
    /// <param name="lpValueName">The name of the registry value.</param>
    /// <param name="lpReserved">This parameter is reserved and must be NULL.</param>
    /// <param name="lpType">
    /// A pointer to a variable that receives a code indicating the type of data
    /// stored in the specified value. For a list of the possible type codes, see Registry Value Types.
    /// The lpType parameter can be NULL if the type code is not required.
    /// </param>
    /// <param name="lpData">
    /// A pointer to a buffer that receives the value's data.
    /// This parameter can be NULL if the data is not required.
    /// </param>
    /// <param name="lpcbData">
    /// A pointer to a variable that specifies the size of the buffer pointed to by the lpData parameter,
    /// in bytes. When the function returns, this variable contains the size of the data copied to lpData.
    /// The lpcbData parameter can be NULL only if lpData is NULL.
    /// </param>
    /// <returns></returns>
    public uint RegQueryValue_Hooked(UIntPtr hKey, [MarshalAs(UnmanagedType.LPWStr)] string lpValueName,
                               IntPtr lpReserved, IntPtr lpType, IntPtr lpData, IntPtr lpcbData)
    {
      // BUG: If lpValueName is NULL or an empty string, the function retrieves the type and data for the key's unnamed or default value, if any.
      if (string.IsNullOrEmpty(lpValueName))
        return WinError.ERROR_FILE_NOT_FOUND;
      uint handle;
      if (!TryParse(hKey, out handle))
        return WinError.ERROR_INVALID_HANDLE;
      using (HookManager.ACL.GetHookingExclusion())
      {
        VirtualRegistryValue virtualRegistryValue;
        var hResult = _registry.QueryValue(handle, lpValueName, out virtualRegistryValue);
        GuestCore.Log(new LogMessage(LogLevel.Debug, "QueryValue(HKey={0} ValueName={1}) => {2}",
                                     handle, lpValueName, hResult));
        if (hResult != NativeResultCode.Succes)
          return WinError.FromStateCode(hResult);
        // Marshal all data to the specified pointers.
        MarshallingHelpers.CopyToMemory(virtualRegistryValue.Type, lpType);
        uint? dataLength = lpcbData != IntPtr.Zero
                           // Valid pointer, copy the 32bit unsigned integer.
                             ? (uint?) lpcbData.Read<uint>()
                           // Invalid pointer, guest doesn't require lpcbData.
                             : null;
        uint winError = MarshallingHelpers.CopyToMemory(virtualRegistryValue.Data, lpData, ref dataLength);
        if (dataLength != null)
          MarshallingHelpers.CopyToMemory(dataLength, lpcbData);
        return winError;
      }
    }

    /// <summary>
    /// This method will insert a value into the VREG.
    /// </summary>
    /// <param name="hKey">A handle to an open registry key.</param>
    /// <param name="lpValueName">
    /// The name of the value to be set. If a value with this name is not already present in the key,
    /// the function adds it to the key.
    /// If lpValueName is NULL or an empty string, "", the function sets the type and data for
    /// the key's unnamed or default value.
    /// </param>
    /// <param name="Reserved">This parameter is reserved and must be zero.</param>
    /// <param name="dwType">The type of data pointed to by the <paramref name="lpData"/> parameter.</param>
    /// <param name="lpData">The data to be stored.</param>
    /// <param name="cbData">The size of the information pointed to by the lpData parameter, in bytes.</param>
    /// <returns>A WinError code.</returns>
    public uint RegSetValueEx(UIntPtr hKey, [MarshalAs(UnmanagedType.LPWStr)] string lpValueName,
                             uint Reserved, ValueType dwType, IntPtr lpData, uint cbData)
    {
      uint handle;
      if (!TryParse(hKey, out handle))
        return WinError.ERROR_INVALID_HANDLE;
      using (HookManager.ACL.GetHookingExclusion())
      {
        var data = lpData.Read<byte[]>(cbData);
        var registryValue = new VirtualRegistryValue(lpValueName, data, dwType);
        var stateCode = _registry.SetValue(handle, registryValue);
        GuestCore.Log(new LogMessage(LogLevel.Debug, "SetValue(HKey={0} Name={1} Type={2}) => {3}",
                                     handle, lpValueName, dwType, stateCode));
        return WinError.FromStateCode(stateCode);
      }
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Tries to parse the <see cref="Int64"/> value of <paramref name="pointer"/> to an <see cref="UInt32"/>
    /// </summary>
    /// <param name="pointer"></param>
    /// <param name="result"></param>
    /// <returns></returns>
    private static bool TryParse(UIntPtr pointer, out uint result)
    {
      result = (uint) pointer;
      return true;
    }

    #endregion

  }
}
