// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Text;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.System.Memory;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;
using Windows.Win32.UI.WindowsAndMessaging;
using WNDPROC = Windows.Win32.Extras.ManagedWNDPROC;

namespace Files.App.Utils.Shell
{
	/// <summary>
	/// Provides a helper for Win32 context menus.
	/// </summary>
	public sealed partial class ContextMenu : Win32ContextMenu, IDisposable
	{
		private const uint CmicMaskUnicode = 0x00004000;
		private static readonly ImageConverter IconConverter = new();

		private IContextMenu? contextMenu;
		private HMENU menu;
		private readonly ContextMenuWorkerPool.Worker worker;
		private readonly Func<string?, bool>? itemFilter;
		private readonly Dictionary<List<Win32ContextMenuItem>, Action> loadSubMenuActions = [];
		private bool disposedValue;

		private ThreadWithMessageQueue OwningThread => worker.Thread;

		private IContextMenu Menu => contextMenu ?? throw new ObjectDisposedException(nameof(ContextMenu));

		public List<string> ItemsPath { get; }

		private ContextMenu(IContextMenu contextMenu, HMENU menu, IEnumerable<string> itemsPath, ContextMenuWorkerPool.Worker worker, Func<string?, bool>? itemFilter)
		{
			this.contextMenu = contextMenu;
			this.menu = menu;
			this.worker = worker;
			this.itemFilter = itemFilter;
			ItemsPath = itemsPath.ToList();
			Items = [];
		}

		public static async Task<bool> InvokeVerb(string verb, params string?[] filePaths)
		{
			using var contextMenu = await GetContextMenuForFiles(filePaths, PInvoke.CMF_DEFAULTONLY);
			return contextMenu is not null && await contextMenu.InvokeVerb(verb);
		}

		public async Task<bool> InvokeVerb(string? verb)
		{
			if (string.IsNullOrEmpty(verb))
				return false;

			var items = Items ?? throw new InvalidOperationException("The shell context menu has not been initialized.");
			var item = items.FirstOrDefault(x => x.CommandString == verb);
			if (item is not null && item.ID >= 0)
				return await InvokeItem(item.ID);

			try
			{
				var currentWindows = Win32Helper.GetDesktopWindows();
				HRESULT result = await OwningThread.PostMethod(() => InvokeVerbCore(verb));
				if (result.Failed)
					return false;
				Win32Helper.BringToForeground(currentWindows);
				return true;
			}
			catch (Exception ex) when (ex is COMException or UnauthorizedAccessException)
			{
				Debug.WriteLine(ex);
				return false;
			}
		}

		public async Task<bool> InvokeItem(int itemId, string? workingDirectory = null)
		{
			if (itemId < 0)
				return false;

			try
			{
				var currentWindows = Win32Helper.GetDesktopWindows();
				HRESULT result = await OwningThread.PostMethod(() => InvokeItemCore(itemId, workingDirectory));
				if (result.Failed)
					return false;
				Win32Helper.BringToForeground(currentWindows);
				return true;
			}
			catch (Exception ex) when (ex is COMException or UnauthorizedAccessException)
			{
				Debug.WriteLine(ex);
				return false;
			}
		}

		private unsafe HRESULT InvokeVerbCore(string verb)
		{
			byte[] verbBytes = Encoding.ASCII.GetBytes(verb + '\0');
			fixed (byte* verbPointer = verbBytes)
			{
				CMINVOKECOMMANDINFOEX commandInfo = default;
				commandInfo.cbSize = (uint)sizeof(CMINVOKECOMMANDINFOEX);
				commandInfo.lpVerb = (PCSTR)verbPointer;
				commandInfo.nShow = (int)SHOW_WINDOW_CMD.SW_SHOWNORMAL;
				return Menu.InvokeCommand((CMINVOKECOMMANDINFO*)&commandInfo);
			}
		}

		private unsafe HRESULT InvokeItemCore(int itemId, string? workingDirectory)
		{
			fixed (char* directory = workingDirectory)
			{
				CMINVOKECOMMANDINFOEX commandInfo = default;
				commandInfo.cbSize = (uint)sizeof(CMINVOKECOMMANDINFOEX);
				commandInfo.fMask = CmicMaskUnicode;
				commandInfo.lpVerb = (PCSTR)(byte*)(nuint)(uint)itemId;
				commandInfo.lpVerbW = (PCWSTR)(char*)(nuint)(uint)itemId;
				commandInfo.lpDirectoryW = directory;
				commandInfo.nShow = (int)SHOW_WINDOW_CMD.SW_SHOWNORMAL;
				return Menu.InvokeCommand((CMINVOKECOMMANDINFO*)&commandInfo);
			}
		}

		public static async Task<ContextMenu?> GetContextMenuForFiles(string?[] filePathList, uint flags, Func<string?, bool>? itemFilter = null)
		{
			var worker = ContextMenuWorkerPool.Rent();
			var contextMenu = await worker.Thread.PostMethod<ContextMenu?>(() =>
			{
				var shellItems = new List<ShellItem>();
				try
				{
					foreach (string path in filePathList.WhereNotNull().Where(path => !string.IsNullOrEmpty(path)))
						shellItems.Add(ShellFolderExtensions.GetShellItemFromPathOrPIDL(path));
					return Create([.. shellItems], flags, worker, itemFilter);
				}
				catch
				{
					return null;
				}
				finally
				{
					foreach (ShellItem item in shellItems)
						item.Dispose();
				}
			});

			if (contextMenu is null)
				ContextMenuWorkerPool.Return(worker);
			return contextMenu;
		}

		public static async Task<ContextMenu?> GetContextMenuForFiles(ShellItem[] shellItems, uint flags, Func<string?, bool>? itemFilter = null)
		{
			var worker = ContextMenuWorkerPool.Rent();
			var contextMenu = await worker.Thread.PostMethod<ContextMenu?>(() => Create(shellItems, flags, worker, itemFilter));
			if (contextMenu is null)
				ContextMenuWorkerPool.Return(worker);
			return contextMenu;
		}

		// Native (Win32) popup menu support

		private const string NativeMenuWindowClassName = "FilesNativeContextMenuHost";
		private static readonly WNDPROC NativeMenuWindowProc = HandleNativeMenuWindowProc;
		private static bool nativeMenuClassRegistered;

		// The shell handler of the menu currently shown on this thread. The menu window's
		// messages arrive on the worker thread that owns the window, so a thread-static slot
		// keeps concurrent workers isolated.
		[ThreadStatic]
		private static IContextMenu? currentNativeMenuHandler;

		/// <summary>
		/// Shows the native Win32 shell context menu (as Explorer does) for the given files at the
		/// screen position and invokes the command the user picks.
		/// </summary>
		/// <param name="filePathList">Paths of the items the menu targets.</param>
		/// <param name="screenX">Menu position in physical screen pixels.</param>
		/// <param name="screenY">Menu position in physical screen pixels.</param>
		/// <param name="ownerWindowHandle">Handle of the app window that owns the interaction.</param>
		/// <returns>Whether a shell command was invoked.</returns>
		public static async Task<bool> ShowNativeMenuAtAsync(string?[] filePathList, int screenX, int screenY, nint ownerWindowHandle)
		{
			var worker = ContextMenuWorkerPool.Rent();
			try
			{
				return await worker.Thread.PostMethod(() =>
				{
					var shellItems = new List<ShellItem>();
					try
					{
						foreach (string path in filePathList.WhereNotNull().Where(path => !string.IsNullOrEmpty(path)))
							shellItems.Add(ShellFolderExtensions.GetShellItemFromPathOrPIDL(path));

						if (shellItems.Count is 0)
							return false;

						return ShowNativeMenuCore([.. shellItems], screenX, screenY, ownerWindowHandle);
					}
					catch
					{
						return false;
					}
					finally
					{
						foreach (ShellItem item in shellItems)
							item.Dispose();
					}
				});
			}
			finally
			{
				ContextMenuWorkerPool.Return(worker);
			}
		}

		private static unsafe bool ShowNativeMenuCore(ShellItem[] shellItems, int screenX, int screenY, nint ownerWindowHandle)
		{
			var ownerHwnd = new HWND(ownerWindowHandle);
			if (ownerHwnd.IsNull || shellItems.Length is 0)
				return false;

			ITEMIDLIST** pidls = null;
			HMENU menu = default;
			HWND menuWindow = default;
			IContextMenu? contextMenu = null;
			uint currentThreadId = PInvoke.GetCurrentThreadId();
			uint ownerThreadId = PInvoke.GetWindowThreadProcessId(ownerHwnd, out _);
			bool threadsAttached = false;

			try
			{
				pidls = (ITEMIDLIST**)NativeMemory.AllocZeroed((nuint)shellItems.Length, (nuint)sizeof(ITEMIDLIST*));
				for (var index = 0; index < shellItems.Length; index++)
					PInvoke.SHGetIDListFromObject(shellItems[index].IShellItem, out pidls[index]).ThrowOnFailure();

				PInvoke.SHCreateShellItemArrayFromIDLists((uint)shellItems.Length, pidls, out IShellItemArray itemArray).ThrowOnFailure();
				contextMenu = BindContextMenu(itemArray);

				menu = PInvoke.CreatePopupMenu();
				contextMenu.QueryContextMenu(menu, 0, 1, 0x7FFF, PInvoke.CMF_NORMAL).ThrowOnFailure();

				// The menu is owned by a hidden window on this worker STA thread so that
				// WM_INITMENUPOPUP / WM_MEASUREITEM / WM_DRAWITEM (needed by handlers that populate
				// or owner-draw their items) are delivered here and can be forwarded to the handler.
				menuWindow = CreateNativeMenuWindow();
				if (menuWindow.IsNull)
					return false;

				currentNativeMenuHandler = contextMenu;

				var desktopWindows = Win32Helper.GetDesktopWindows();

				// Sharing the input queue with the (foreground) UI thread is required: without it
				// the system immediately dismisses menus shown from a background thread.
				threadsAttached = ownerThreadId != 0 && ownerThreadId != currentThreadId
					&& PInvoke.AttachThreadInput(currentThreadId, ownerThreadId, true);
				try
				{
					PInvoke.SetForegroundWindow(ownerHwnd);

					var flags =
						TRACK_POPUP_MENU_FLAGS.TPM_RETURNCMD |
						TRACK_POPUP_MENU_FLAGS.TPM_RIGHTBUTTON |
						(PInvoke.GetSystemMetricsForDpi(SYSTEM_METRICS_INDEX.SM_MENUDROPALIGNMENT, PInvoke.GetDpiForWindow(ownerHwnd)) != 0
							? TRACK_POPUP_MENU_FLAGS.TPM_RIGHTALIGN
							: 0);

					var command = PInvoke.TrackPopupMenuEx(menu, (uint)flags, screenX, screenY, menuWindow, null).Value;
					if (command is 0)
						return false;

					// lpVerb is the offset relative to the idCmdFirst (1) passed to QueryContextMenu,
					// not the absolute menu item id; without the shift every command runs its neighbor.
					var commandOffset = command - 1;

					var commandInfo = default(CMINVOKECOMMANDINFOEX);
					commandInfo.cbSize = (uint)sizeof(CMINVOKECOMMANDINFOEX);
					commandInfo.fMask = CmicMaskUnicode;
					commandInfo.lpVerb = (PCSTR)(byte*)(nuint)(uint)commandOffset;
					commandInfo.lpVerbW = (PCWSTR)(char*)(nuint)(uint)commandOffset;
					commandInfo.nShow = (int)SHOW_WINDOW_CMD.SW_SHOWNORMAL;
					contextMenu.InvokeCommand((CMINVOKECOMMANDINFO*)&commandInfo).ThrowOnFailure();

					return true;
				}
				finally
				{
					currentNativeMenuHandler = null;

					if (threadsAttached)
						PInvoke.AttachThreadInput(currentThreadId, ownerThreadId, false);

					// Give focus back to windows that existed before the command ran, so a newly
					// launched window keeps the foreground.
					Win32Helper.BringToForeground(desktopWindows);
				}
			}
			catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or InvalidCastException)
			{
				return false;
			}
			finally
			{
				if (!menuWindow.IsNull)
					PInvoke.DestroyWindow(menuWindow);

				if (!menu.IsNull)
					PInvoke.DestroyMenu(menu);

				if (pidls is not null)
				{
					for (var index = 0; index < shellItems.Length; index++)
						PInvoke.CoTaskMemFree(pidls[index]);
					NativeMemory.Free(pidls);
				}

				if ((object?)contextMenu is ComObject comObject)
					comObject.FinalRelease();
			}
		}

		private static unsafe HWND CreateNativeMenuWindow()
		{
			var moduleHandle = PInvoke.GetModuleHandle(default(PCWSTR));

			if (!nativeMenuClassRegistered)
			{
				var procPointer = Marshal.GetFunctionPointerForDelegate(NativeMenuWindowProc);
				var windowProc = (delegate* unmanaged[Stdcall]<HWND, uint, WPARAM, LPARAM, LRESULT>)procPointer;

				fixed (char* className = NativeMenuWindowClassName)
				{
					var windowClass = new WNDCLASSEXW
					{
						cbSize = (uint)sizeof(WNDCLASSEXW),
						lpfnWndProc = windowProc,
						hInstance = moduleHandle,
						lpszClassName = className,
					};

					// Registering again fails with ERROR_CLASS_ALREADY_EXISTS, which is harmless:
					// the static delegate keeps the registered proc thunk alive for the process.
					PInvoke.RegisterClassEx(in windowClass);
				}

				nativeMenuClassRegistered = true;
			}

			// A never-shown top-level window; only its messages matter.
			return PInvoke.CreateWindowEx(
				WINDOW_EX_STYLE.WS_EX_LEFT,
				NativeMenuWindowClassName,
				string.Empty,
				WINDOW_STYLE.WS_OVERLAPPED,
				0,
				0,
				1,
				1,
				default,
				null,
				null,
				null);
		}

		private static LRESULT HandleNativeMenuWindowProc(HWND hWnd, uint uMsg, WPARAM wParam, LPARAM lParam)
		{
			if (uMsg is PInvoke.WM_INITMENUPOPUP or PInvoke.WM_MEASUREITEM or PInvoke.WM_DRAWITEM)
			{
				var handler = currentNativeMenuHandler;
				if (handler is not null && ForwardMenuMessage(handler, uMsg, wParam, lParam))
					return default;
			}

			return PInvoke.DefWindowProc(hWnd, uMsg, wParam, lParam);
		}

		private static unsafe bool ForwardMenuMessage(IContextMenu handler, uint message, WPARAM wParam, LPARAM lParam)
		{
			try
			{
				LRESULT result = default;
				if (handler is IContextMenu3 contextMenu3)
					return contextMenu3.HandleMenuMsg2(message, wParam, lParam, &result).Succeeded;
				if (handler is IContextMenu2 contextMenu2)
					return contextMenu2.HandleMenuMsg(message, wParam, lParam).Succeeded;
			}
			catch (Exception ex) when (ex is COMException or InvalidCastException or NotImplementedException)
			{
				// The handler cannot process the message; let DefWindowProc run instead.
			}

			return false;
		}

		private static unsafe ContextMenu? Create(ShellItem[] shellItems, uint flags, ContextMenuWorkerPool.Worker worker, Func<string?, bool>? itemFilter)
		{
			if (shellItems.Length is 0)
				return null;

			ITEMIDLIST** pidls = null;
			HMENU menu = default;
			try
			{
				pidls = (ITEMIDLIST**)NativeMemory.AllocZeroed((nuint)shellItems.Length, (nuint)sizeof(ITEMIDLIST*));
				for (int index = 0; index < shellItems.Length; index++)
					PInvoke.SHGetIDListFromObject(shellItems[index].IShellItem, out pidls[index]).ThrowOnFailure();

				PInvoke.SHCreateShellItemArrayFromIDLists((uint)shellItems.Length, pidls, out IShellItemArray itemArray).ThrowOnFailure();
				IContextMenu shellContextMenu = BindContextMenu(itemArray);

				menu = PInvoke.CreatePopupMenu();
				shellContextMenu.QueryContextMenu(menu, 0, 1, 0x7FFF, flags).ThrowOnFailure();
				var contextMenu = new ContextMenu(shellContextMenu, menu, shellItems.Select(item => item.ParsingName).WhereNotNull(), worker, itemFilter);
				menu = default;
				contextMenu.EnumMenuItems(contextMenu.menu, contextMenu.Items!);
				return contextMenu;
			}
			catch (COMException)
			{
				return null;
			}
			finally
			{
				if (!menu.IsNull)
					PInvoke.DestroyMenu(menu);
				if (pidls is not null)
				{
					for (int index = 0; index < shellItems.Length; index++)
						PInvoke.CoTaskMemFree(pidls[index]);
					NativeMemory.Free(pidls);
				}
			}
		}

		private static unsafe IContextMenu BindContextMenu(IShellItemArray itemArray)
		{
			void* itemArrayPointer = ComInterfaceMarshaller<IShellItemArray>.ConvertToUnmanaged(itemArray);
			void* contextMenuPointer = null;
			try
			{
				// Bind through the native vtable so the result can use a uniquely owned generated COM wrapper.
				void** vtable = *(void***)itemArrayPointer;
				var bindToHandler =
					(delegate* unmanaged[MemberFunction]<void*, void*, Guid*, Guid*, void**, int>)vtable[3];
				Guid handlerId = PInvoke.BHID_SFUIObject;
				Guid interfaceId = typeof(IContextMenu).GUID;
				HRESULT result = new(bindToHandler(itemArrayPointer, null, &handlerId, &interfaceId, &contextMenuPointer));
				result.ThrowOnFailure();
				return UniqueComInterfaceMarshaller<IContextMenu>.ConvertToManaged(contextMenuPointer)
					?? throw new InvalidOperationException("The shell did not return a context menu.");
			}
			finally
			{
				UniqueComInterfaceMarshaller<IContextMenu>.Free(contextMenuPointer);
				ComInterfaceMarshaller<IShellItemArray>.Free(itemArrayPointer);
			}
		}

		public static async Task WarmUpQueryContextMenuAsync()
		{
			using var contextMenu = await GetContextMenuForFiles([$@"{Constants.UserEnvironmentPaths.SystemDrivePath}\"], PInvoke.CMF_NORMAL);
		}

		private unsafe void EnumMenuItems(HMENU targetMenu, List<Win32ContextMenuItem> result, bool loadSubmenus = false)
		{
			uint itemCount = unchecked((uint)PInvoke.GetMenuItemCount(targetMenu));
			if (itemCount is unchecked((uint)-1))
				return;

			for (uint index = 0; index < itemCount; index++)
			{
				const uint bufferLength = 512;
				MENUITEMINFOW info = default;
				info.cbSize = (uint)sizeof(MENUITEMINFOW);
				info.fMask = MENU_ITEM_MASK.MIIM_BITMAP | MENU_ITEM_MASK.MIIM_FTYPE | MENU_ITEM_MASK.MIIM_STRING | MENU_ITEM_MASK.MIIM_ID | MENU_ITEM_MASK.MIIM_SUBMENU;
				info.dwTypeData = (char*)NativeMemory.AllocZeroed(bufferLength, sizeof(char));
				info.cch = bufferLength - 1;

				try
				{
					if (!PInvoke.GetMenuItemInfo(targetMenu, index, true, &info))
						continue;

					var menuItem = new ContextMenuItem { Type = (MENU_ITEM_TYPE)info.fType, ID = (int)(info.wID - 1) };
					if (menuItem.Type == MENU_ITEM_TYPE.MFT_STRING)
					{
						menuItem.Label = info.dwTypeData.ToString();
						menuItem.CommandString = GetCommandString(Menu, info.wID - 1);
						if (itemFilter is not null && (itemFilter(menuItem.CommandString) || itemFilter(menuItem.Label)))
							continue;

						if (!info.hbmpItem.IsNull && !Enum.IsDefined((HBITMAP_HMENU)((IntPtr)info.hbmpItem).ToInt64()))
						{
							using Bitmap? bitmap = Win32Helper.GetBitmapFromHBitmap(info.hbmpItem);
							if (bitmap is not null)
							{
								bitmap.MakeTransparent();
								if (IconConverter.ConvertTo(bitmap, typeof(byte[])) is byte[] icon)
									menuItem.Icon = icon;
							}
						}

						if (!info.hSubMenu.IsNull)
						{
							var subItems = new List<Win32ContextMenuItem>();
							HMENU subMenu = info.hSubMenu;
							menuItem.SubItems = subItems;
							if (loadSubmenus)
								LoadSubMenu();
							else
								loadSubMenuActions.Add(subItems, LoadSubMenu);

							void LoadSubMenu()
							{
								try
								{
									if (Menu is IContextMenu2 contextMenu2)
										contextMenu2.HandleMenuMsg(PInvoke.WM_INITMENUPOPUP, (WPARAM)(nuint)subMenu.Value, (LPARAM)(nint)index);
								}
								catch (Exception ex) when (ex is InvalidCastException or ArgumentException or COMException or NotImplementedException)
								{
									Debug.WriteLine(ex);
								}
								EnumMenuItems(subMenu, subItems, true);
							}
						}
					}

					result.Add(menuItem);
				}
				finally
				{
					NativeMemory.Free(info.dwTypeData);
				}
			}
		}

		public Task<bool> LoadSubMenu(List<Win32ContextMenuItem> subItems)
		{
			if (!loadSubMenuActions.Remove(subItems, out Action? loadSubMenu))
				return Task.FromResult(false);

			return OwningThread.PostMethod(() =>
			{
				try
				{
					loadSubMenu();
					return true;
				}
				catch
				{
					return false;
				}
			});
		}

		private static unsafe string? GetCommandString(IContextMenu contextMenu, uint offset)
		{
			// Avoid an AccessViolationException from handlers that return abnormally large command offsets,
			// notably the "Run with graphics processor" menu item from NVIDIA.
			if (offset > 5000)
				return null;

			const int capacity = 512;
			char* buffer = (char*)NativeMemory.AllocZeroed((nuint)capacity, sizeof(char));
			try
			{
				return contextMenu.GetCommandString(offset, PInvoke.GCS_VERBW, (PSTR)(byte*)buffer, capacity).Succeeded ? new string(buffer) : null;
			}
			catch (Exception ex) when (ex is InvalidCastException or ArgumentException or COMException or NotImplementedException)
			{
				Debug.WriteLine(ex);
				return null;
			}
			finally
			{
				NativeMemory.Free(buffer);
			}
		}

		private void Dispose(bool disposing)
		{
			if (disposedValue)
				return;

			if (disposing && Items is not null)
			{
				foreach (Win32ContextMenuItem item in Items)
					(item as IDisposable)?.Dispose();
			}

			// Release the native menu on the worker's own STA thread. The message queue is FIFO,
			// so cleanup completes before work posted by the next renter.
			HMENU menuToDestroy = menu;
			IContextMenu? contextMenuToRelease = contextMenu;
			menu = default;
			contextMenu = null;
			OwningThread.PostMethod(() =>
			{
				if (!menuToDestroy.IsNull)
					PInvoke.DestroyMenu(menuToDestroy);
				if ((object?)contextMenuToRelease is ComObject comObject)
					comObject.FinalRelease();
			});
			ContextMenuWorkerPool.Return(worker);
			disposedValue = true;
		}

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		~ContextMenu()
		{
			Dispose(false);
		}
	}
}
