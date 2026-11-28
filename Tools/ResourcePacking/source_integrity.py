"""Source-input verification. A mismatch fails; expected hashes are never replaced."""
import os,hashlib
def sha(path):
    with path.open('rb') as stream:return hashlib.file_digest(stream,'sha256').hexdigest()
def direct_sha(path):
    if os.name!='nt':return sha(path)
    import ctypes
    from ctypes import wintypes as w
    k=ctypes.WinDLL('kernel32',use_last_error=True)
    k.CreateFileW.argtypes=[w.LPCWSTR,w.DWORD,w.DWORD,ctypes.c_void_p,w.DWORD,w.DWORD,w.HANDLE];k.CreateFileW.restype=w.HANDLE
    k.VirtualAlloc.argtypes=[ctypes.c_void_p,ctypes.c_size_t,w.DWORD,w.DWORD];k.VirtualAlloc.restype=ctypes.c_void_p
    k.VirtualFree.argtypes=[ctypes.c_void_p,ctypes.c_size_t,w.DWORD];k.VirtualFree.restype=w.BOOL
    k.ReadFile.argtypes=[w.HANDLE,ctypes.c_void_p,w.DWORD,ctypes.POINTER(w.DWORD),ctypes.c_void_p];k.ReadFile.restype=w.BOOL
    k.CloseHandle.argtypes=[w.HANDLE]
    handle=k.CreateFileW(str(path),0x80000000,7,None,3,0x20000080,None)
    if handle==ctypes.c_void_p(-1).value:raise ctypes.WinError(ctypes.get_last_error())
    buffer=k.VirtualAlloc(None,1048576,0x3000,4)
    if not buffer:k.CloseHandle(handle);raise ctypes.WinError(ctypes.get_last_error())
    try:
        remaining=path.stat().st_size;checksum=hashlib.sha256()
        while remaining:
            size=w.DWORD();count=min(1048576,((remaining+4095)//4096)*4096)
            if not k.ReadFile(handle,buffer,count,ctypes.byref(size),None):raise ctypes.WinError(ctypes.get_last_error())
            if not size.value:raise IOError('Unexpected EOF')
            used=min(remaining,size.value);checksum.update(ctypes.string_at(buffer,used));remaining-=used
        return checksum.hexdigest()
    finally:k.VirtualFree(buffer,0,0x8000);k.CloseHandle(handle)
