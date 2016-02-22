/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Reciprocal Community License ("RCL") Version 1.00
 * 
 * Unless explicitly acquired and licensed from Licensor under another 
 * license, the contents of this file are subject to the Reciprocal 
 * Community License ("RCL") Version 1.00, or subsequent versions 
 * as allowed by the RCL, and You may not copy or use this file in either 
 * source code or executable form, except in compliance with the terms and 
 * conditions of the RCL.
 * 
 * All software distributed under the RCL is provided strictly on an 
 * "AS IS" basis, WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESS OR IMPLIED, 
 * AND LICENSOR HEREBY DISCLAIMS ALL SUCH WARRANTIES, INCLUDING WITHOUT 
 * LIMITATION, ANY WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR 
 * PURPOSE, QUIET ENJOYMENT, OR NON-INFRINGEMENT. See the RCL for specific 
 * language governing rights and limitations under the RCL.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/RCL/1.00/
 * ======================================================================*/

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text;
using System.Xml;

namespace Opc.Ua
{
    /// <summary>
    /// A generic user identity class.
    /// </summary>
    public class UserIdentity : IUserIdentity
    {
        #region Constructors
        /// <summary>
        /// Initializes the object as an anonymous user.
        /// </summary>
        public UserIdentity()
        {
            m_tokenType = UserTokenType.Anonymous;
            m_displayName = "Anonymous";
        }

        /// <summary>
        /// Initializes the object with a username and password.
        /// </summary>
        /// <param name="username">The user name.</param>
        /// <param name="password">The password.</param>
        public UserIdentity(string username, string password)
        {
        }

        /// <summary>
        /// Initializes the object with an X509 certificate identifier
        /// </summary>
        /// <param name="certificateId">The certificate identifier.</param>
        public UserIdentity(CertificateIdentifier certificateId)
        {
        }

        /// <summary>
        /// Initializes the object with an X509 certificate
        /// </summary>
        /// <param name="certificate">The X509 certificate.</param>
        public UserIdentity(X509Certificate2 certificate)
        {
        }

        /// <summary>
        /// Initializes the object with a UA identity token.
        /// </summary>
        /// <param name="token">The user identity token.</param>
        public UserIdentity(UserIdentityToken token)
        {
            Initialize(token);
        }
        #endregion

        #region IUserIdentity Methods
        /// <summary>
        /// Gets or sets the UserIdentityToken PolicyId associated with the UserIdentity.
        /// </summary>
        /// <remarks>
        /// This value is used to initialize the UserIdentityToken object when GetIdentityToken() is called.
        /// </remarks>
        public string PolicyId
        {
            get { return m_policyId; }
            set { m_policyId = value; }
        }
        #endregion

        #region IUserIdentity Methods
        /// <summary cref="IUserIdentity.DisplayName" />
        public string DisplayName
        {
            get { return m_displayName; }
        }

        /// <summary cref="IUserIdentity.TokenType" />
        public UserTokenType TokenType
        {
            get { return m_tokenType; }
        }
        
        /// <summary cref="IUserIdentity.IssuedTokenType" />
        public XmlQualifiedName IssuedTokenType
        {
            get { return m_issuedTokenType; }
        }        

        /// <summary cref="IUserIdentity.SupportsSignatures" />
        public bool SupportsSignatures
        {
            get  
            {
                return false; 
            }
        }

        /// <summary cref="IUserIdentity.GetIdentityToken" />
        public UserIdentityToken GetIdentityToken()
        {
            AnonymousIdentityToken token = new AnonymousIdentityToken();
            token.PolicyId = m_policyId;
            return token;
        }
        #endregion
        
        #region Private Methods
        /// <summary>
        /// Initializes the object with a UA identity token
        /// </summary>
        private void Initialize(UserIdentityToken token)
        {
            if (token == null) throw new ArgumentNullException("token");

            m_policyId = token.PolicyId;
  
            UserNameIdentityToken usernameToken = token as UserNameIdentityToken;

            AnonymousIdentityToken anonymousToken = token as AnonymousIdentityToken;

            if (anonymousToken != null)
            {
                m_tokenType = UserTokenType.Anonymous;
                m_issuedTokenType = null;
                m_displayName = "Anonymous";
                return;
            }        
  
            throw new ArgumentException("Unrecognized UA user identity token type.", "token");
        }
        #endregion
        
        #region WIN32 Function Declarations
        private static class Win32
        {
            public const int LOGON32_PROVIDER_DEFAULT = 0;
            public const int LOGON32_LOGON_INTERACTIVE = 2;
            public const int LOGON32_LOGON_NETWORK = 3;

            [DllImport("advapi32.dll", SetLastError = true)]
            public static extern int LogonUserW(
                [MarshalAs(UnmanagedType.LPWStr)]
                string lpszUsername,
                [MarshalAs(UnmanagedType.LPWStr)]
                string lpszDomain,
                [MarshalAs(UnmanagedType.LPWStr)]
                string lpszPassword,
                int dwLogonType,
                int dwLogonProvider,
                ref IntPtr phToken);

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
            public extern static int CloseHandle(IntPtr handle);
        }
        #endregion
       
        #region Private Fields
        private string m_displayName;
        private UserTokenType m_tokenType;
        private XmlQualifiedName m_issuedTokenType;
        private string m_policyId;
        #endregion
    }

    #region ImpersonationContext Class
    /// <summary>
    /// Stores information about the user that is currently being impersonated.
    /// </summary>
    public class ImpersonationContext : IDisposable
    {
        #region Public Members
        /// <summary>
        /// The security principal being impersonated.
        /// </summary>
        public IPrincipal Principal { get; set; }
        #endregion

        #region Internal Members
        internal IntPtr Handle { get; set; }
        #endregion

        #region IDisposable Members
        /// <summary>
        /// The finializer implementation.
        /// </summary>
        ~ImpersonationContext()
        {
            Dispose(false);
        }

        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #region PInvoke Declarations
        private static class Win32
        {
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
            public extern static bool CloseHandle(IntPtr handle);
        }
        #endregion

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (Handle != IntPtr.Zero)
            {
                Win32.CloseHandle(Handle);
                Handle = IntPtr.Zero;
            }
        }
        #endregion
    }
    #endregion
}
