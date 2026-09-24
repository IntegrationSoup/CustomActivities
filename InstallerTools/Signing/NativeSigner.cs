using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography.X509Certificates;

public static class UnattendedSigner
{
    [StructLayout(LayoutKind.Sequential)] struct Provider { public IntPtr container, name; public uint type, flags, count; public IntPtr parameters; public uint spec; }
    [StructLayout(LayoutKind.Sequential)] struct FileInfo { public uint size; public IntPtr name, handle; }
    [StructLayout(LayoutKind.Sequential)] struct Subject { public uint size; public IntPtr index; public uint choice; public IntPtr file; }
    [StructLayout(LayoutKind.Sequential)] struct StoreInfo { public uint size; public IntPtr cert; public uint policy; public IntPtr store; }
    [StructLayout(LayoutKind.Sequential)] struct SignerCert { public uint size, choice; public IntPtr info, window; }
    [StructLayout(LayoutKind.Sequential)] struct Signature { public uint size, algorithm, choice; public IntPtr attributes, authenticated, unauthenticated; }
    [StructLayout(LayoutKind.Sequential)] struct Authcode { public uint size; public int commercial, individual; public IntPtr name, info; }
    [DllImport("crypt32.dll", SetLastError=true)] static extern bool CertGetCertificateContextProperty(IntPtr cert, uint property, IntPtr data, ref uint size);
    [DllImport("crypt32.dll", SetLastError=true)] static extern bool CertSetCertificateContextProperty(IntPtr cert, uint property, uint flags, IntPtr data);
    [DllImport("crypt32.dll", SetLastError=true)] static extern IntPtr CertCreateCertificateContext(uint encoding, byte[] bytes, uint size);
    [DllImport("crypt32.dll")] static extern bool CertFreeCertificateContext(IntPtr cert);
    [DllImport("crypt32.dll", SetLastError=true)] static extern bool CryptAcquireCertificatePrivateKey(IntPtr cert, uint flags, IntPtr reserved, out IntPtr key, out uint spec, out bool free);
    [DllImport("ncrypt.dll", CharSet=CharSet.Unicode)] static extern int NCryptSetProperty(IntPtr key, string property, IntPtr data, uint size, uint flags);
    [DllImport("mssign32.dll", CharSet=CharSet.Unicode)] static extern int SignerSignEx2(uint flags, ref Subject subject, ref SignerCert cert, ref Signature signature, IntPtr provider, uint timestampFlags, [MarshalAs(UnmanagedType.LPStr)] string timestampOid, string timestampUrl, IntPtr request, IntPtr sip, out IntPtr context, IntPtr policy, IntPtr reserved);
    [DllImport("mssign32.dll")] static extern int SignerFreeSignerContext(IntPtr context);
    static void Check(bool ok, string stage) { if (!ok) throw new Exception(stage + " failed: 0x" + Marshal.GetLastWin32Error().ToString("X8")); }
    static uint Size<T>() { return (uint)Marshal.SizeOf<T>(); }
    static IntPtr Put<T>(T value, List<IntPtr> allocations) { var p=Marshal.AllocHGlobal(Marshal.SizeOf<T>()); allocations.Add(p); Marshal.StructureToPtr(value,p,false); return p; }

    // This context is detached from the certificate store: silent flags are never persisted.
    public static void Sign(X509Certificate2 source, string path, SecureString pin, string description)
    {
        var allocations=new List<IntPtr>(); IntPtr cert=IntPtr.Zero, context=IntPtr.Zero;
        try {
            byte[] raw=source.RawData;
            cert=CertCreateCertificateContext(0x10001,raw,(uint)raw.Length); Check(cert!=IntPtr.Zero,"Create certificate context");
            uint length=0; Check(CertGetCertificateContextProperty(source.Handle,2,IntPtr.Zero,ref length),"Read provider size");
            var provider=Marshal.AllocHGlobal((int)length); allocations.Add(provider);
            Check(CertGetCertificateContextProperty(source.Handle,2,provider,ref length),"Read provider");
            var info=Marshal.PtrToStructure<Provider>(provider);
            if(info.type!=0 || Marshal.PtrToStringUni(info.name)!="Microsoft Smart Card Key Storage Provider") throw new Exception("Unexpected provider; no authentication attempted.");
            info.flags |= 0x40; // NCRYPT_SILENT_FLAG
            Marshal.StructureToPtr(info,provider,false);
            Check(CertSetCertificateContextProperty(cert,2,0,provider),"Set isolated silent provider");
            IntPtr key; uint spec; bool free;
            Check(CryptAcquireCertificatePrivateKey(cert,0x40000|0x40|0x1,IntPtr.Zero,out key,out spec,out free),"Acquire silent CNG key");
            if(spec!=0xFFFFFFFF || free) throw new Exception("Unexpected key ownership or technology.");
            if(pin!=null) {
                var secret=Marshal.SecureStringToGlobalAllocUnicode(pin);
                try { int status=NCryptSetProperty(key,"SmartCardPin",secret,(uint)((pin.Length+1)*2),0x40); if(status!=0) throw new Exception("Set PIN failed: 0x"+status.ToString("X8")); }
                finally { Marshal.ZeroFreeGlobalAllocUnicode(secret); }
            }
            var name=Marshal.StringToHGlobalUni(path); allocations.Add(name);
            var file=Put(new FileInfo {size=Size<FileInfo>(),name=name},allocations);
            var index=Marshal.AllocHGlobal(4); allocations.Add(index); Marshal.WriteInt32(index,0);
            var subject=new Subject {size=Size<Subject>(),index=index,choice=1,file=file};
            var store=Put(new StoreInfo {size=Size<StoreInfo>(),cert=cert,policy=8},allocations);
            var signer=new SignerCert {size=Size<SignerCert>(),choice=2,info=store};
            var signature=new Signature {size=Size<Signature>(),algorithm=0x800C}; // SHA256
            if(!String.IsNullOrEmpty(description)) {
                var displayName=Marshal.StringToHGlobalUni(description); allocations.Add(displayName);
                signature.choice=1;
                signature.attributes=Put(new Authcode {size=Size<Authcode>(),name=displayName},allocations);
            }
            int result=SignerSignEx2(0,ref subject,ref signer,ref signature,IntPtr.Zero,0,null,null,IntPtr.Zero,IntPtr.Zero,out context,IntPtr.Zero,IntPtr.Zero);
            if(result!=0) throw new Exception("SignerSignEx2 failed: 0x"+result.ToString("X8"));
        }
        finally {
            if(context!=IntPtr.Zero) SignerFreeSignerContext(context);
            if(cert!=IntPtr.Zero) CertFreeCertificateContext(cert);
            foreach(var p in allocations) Marshal.FreeHGlobal(p);
        }
    }
}
