// ZoundsDependencyCheck.cs
// Provides a clear compile-time message when Addressables is missing.

#if !ADDRESSABLES_INSTALLED
#warning Zounds requires the Addressables package. Install via: Window > Package Manager > + > Add package by name > com.unity.addressables
#endif
