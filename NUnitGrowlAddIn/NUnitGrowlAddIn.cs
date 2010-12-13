using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Growl.Connector;
using Growl.CoreLibrary;
using NUnit.Core;
using NUnit.Core.Extensibility;
using NUnitGrowlAddIn.Properties;

namespace NUnitGrowlAddIn
{
    /// <summary>
    ///     NUnitGrowlAddIn class.
    /// </summary>
    [NUnitAddin(
        Type = ExtensionType.Client | ExtensionType.Core | ExtensionType.Gui,
        Name = "NUnit Add-in for Growl Notification",
        Description = "NUnit Add-in that sends notifications about test runs to Growl.")]
    public sealed class NUnitGrowlAddIn : IAddin, EventListener
    {
        #region Constants

        private const string c_testRunStarted = "NUnit.TestRun.Started";
        private const string c_testRunSucceeded = "NUnit.TestRun.Succeeded";
        private const string c_testRunFailed = "NUnit.TestRun.Failed";
        private const string c_testRunFirstTestFailed = "NUnit.TestRun.FirstTestFailed";

        #endregion

        #region Fields

        private readonly GrowlConnector m_growlConnector;
        private readonly Application m_growlApplication;
        private bool m_isFirstTestFailed;
        private string m_lastTestRunFullName;

        #endregion

        #region Constructors

        /// <summary>
        ///     Initializes a new instance of the <see cref="NUnitGrowlAddIn"/> class.
        /// </summary>
        public NUnitGrowlAddIn()
        {
            m_growlConnector = new GrowlConnector();
            if (!GrowlConnector.IsGrowlRunningLocally())
            {
                return;
            }

            m_growlApplication = new Application("NUnit");
            NotificationType[] notificationTypes = new NotificationType[]
            {
                new NotificationType(c_testRunStarted),
                new NotificationType(c_testRunSucceeded),
                new NotificationType(c_testRunFailed),
                new NotificationType(c_testRunFirstTestFailed)
            };

            m_growlConnector.Register(m_growlApplication, notificationTypes);
        }

        #endregion

        #region Private Methods

        // NOTE: Any values sent to Growl (including the notification title, text, callback data, etc) must *NOT*
        // contain any carriage return/line feed character sequences (\r\n). This sequence is used as the end-of-line
        // delimiter in the GNTP protocol. If you want to send a line break as part of your value, you should use
        // a single newline character (\n).

        private static string GetNewMessageId()
        {
            return Guid.NewGuid().ToString();
        }

        private void NotifyTestRunStarted(string name, int testCount)
        {
            string message = string.Format(
                "{0}:\n{1} test{2}.",
                Path.GetFileName(name),
                testCount,
                testCount == 1 ? string.Empty : "s");

            string messageId = GetNewMessageId();
            Notification notification = new Notification(
                m_growlApplication.Name,
                c_testRunStarted,
                messageId,
                "NUnit test run has started",
                message,
                null,
                false,
                Priority.Normal,
                messageId);
            m_growlConnector.Notify(notification);
        }

        private void NotifyTestRunSuccess(TestResult result)
        {
            string message = Path.GetFileName(result.FullName);

            string messageId = GetNewMessageId();
            Notification notification = new Notification(
                m_growlApplication.Name,
                c_testRunSucceeded,
                messageId,
                "NUnit test run has finished successfully.",
                message,
                null,
                false,
                Priority.Normal,
                messageId);
            m_growlConnector.Notify(notification);
        }

        private void NotifyTestRunFailure(TestResult result)
        {
            string message = Path.GetFileName(result.FullName);

            string messageId = GetNewMessageId();
            Notification notification = new Notification(
                m_growlApplication.Name,
                c_testRunFailed,
                messageId,
                "NUnit test run has failed.",
                message,
                Resources.Error,
                false,
                Priority.High,
                messageId);
            m_growlConnector.Notify(notification);
        }

        private void NotifyFirstTestFailure(TestResult result)
        {
            string message = string.Format(
                "Test \"{0}\" has failed.\n\n" +
                    "Continuing running...",
                result.FullName);

            string messageId = GetNewMessageId();
            Notification notification = new Notification(
                m_growlApplication.Name,
                c_testRunFirstTestFailed,
                messageId,
                "NUnit test run: first failed test.",
                message,
                Resources.Warning,
                false,
                Priority.Moderate,
                messageId);
            m_growlConnector.Notify(notification);
        }

        #endregion

        #region IAddin Members

        public bool Install(IExtensionHost host)
        {
            #region Argument Check
            if (host == null)
            {
                throw new ArgumentNullException("host");
            }
            #endregion

            IExtensionPoint eventListenersPoint = host.GetExtensionPoint("EventListeners");
            if (eventListenersPoint == null)
            {
                return false;
            }

            eventListenersPoint.Install(this);
            return true;
        }

        #endregion

        #region EventListener Members

        public void RunFinished(Exception exception)
        {
            #region Argument Check
            if (exception == null)
            {
                throw new ArgumentNullException("exception");
            }
            #endregion

            if (m_growlConnector == null || !GrowlConnector.IsGrowlRunningLocally())
            {
                return;
            }

            TestResult result = new TestResult(new TestName() { FullName = m_lastTestRunFullName });
            result.Error(exception);

            this.NotifyTestRunFailure(result);
        }

        public void RunFinished(TestResult result)
        {
            #region Argument Check
            if (result == null)
            {
                throw new ArgumentNullException("result");
            }
            #endregion

            if (m_growlConnector == null || !GrowlConnector.IsGrowlRunningLocally())
            {
                return;
            }

            if (result.IsSuccess)
            {
                this.NotifyTestRunSuccess(result);
            }
            else
            {
                this.NotifyTestRunFailure(result);
            }
        }

        public void RunStarted(string name, int testCount)
        {
            m_isFirstTestFailed = false;
            m_lastTestRunFullName = name;

            if (m_growlConnector == null || !GrowlConnector.IsGrowlRunningLocally())
            {
                return;
            }

            if (!Settings.Default.NotifyTestRunStarted)
            {
                return;
            }

            this.NotifyTestRunStarted(name, testCount);
        }

        public void SuiteFinished(TestResult result)
        {
            // Nothing to do
        }

        public void SuiteStarted(TestName testName)
        {
            // Nothing to do
        }

        public void TestFinished(TestResult result)
        {
            #region Argument Check
            if (result == null)
            {
                throw new ArgumentNullException("result");
            }
            #endregion

            if (m_isFirstTestFailed || !Settings.Default.NotifyFirstTestFailed || result.IsSuccess)
            {
                return;
            }

            m_isFirstTestFailed = true;

            if (m_growlConnector == null || !GrowlConnector.IsGrowlRunningLocally())
            {
                return;
            }

            this.NotifyFirstTestFailure(result);
        }

        public void TestOutput(TestOutput testOutput)
        {
            // Nothing to do
        }

        public void TestStarted(TestName testName)
        {
            // Nothing to do
        }

        public void UnhandledException(Exception exception)
        {
            // Nothing to do
        }

        #endregion
    }
}