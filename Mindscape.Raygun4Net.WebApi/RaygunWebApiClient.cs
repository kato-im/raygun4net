﻿using System;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Web.Http;
using System.Web.Http.Controllers;
using System.Web.Http.Dispatcher;
using System.Web.Http.ExceptionHandling;
using Mindscape.Raygun4Net.WebApi.Builders;
using System.Collections.Generic;
using Mindscape.Raygun4Net.Messages;
using System.Net;
using System.Collections;
using System.Linq;

namespace Mindscape.Raygun4Net.WebApi
{
  public class RaygunWebApiClient : RaygunClientBase
  {
    private readonly string _apiKey;
    protected readonly RaygunRequestMessageOptions _requestMessageOptions = new RaygunRequestMessageOptions();
    private readonly List<Type> _wrapperExceptions = new List<Type>();

    private readonly ThreadLocal<HttpRequestMessage> _currentWebRequest = new ThreadLocal<HttpRequestMessage>(() => null);
    private readonly ThreadLocal<RaygunRequestMessage> _currentRequestMessage = new ThreadLocal<RaygunRequestMessage>(() => null);

    private static RaygunWebApiExceptionFilter _exceptionFilter;
    private static RaygunWebApiActionFilter _actionFilter;

    /// <summary>
    /// Initializes a new instance of the <see cref="RaygunClientBase" /> class.
    /// </summary>
    /// <param name="apiKey">The API key.</param>
    public RaygunWebApiClient(string apiKey)
    {
      _apiKey = apiKey;

      _wrapperExceptions.Add(typeof(TargetInvocationException));

      if (!string.IsNullOrEmpty(RaygunSettings.Settings.IgnoreFormFieldNames))
      {
        var ignoredNames = RaygunSettings.Settings.IgnoreFormFieldNames.Split(',');
        IgnoreFormFieldNames(ignoredNames);
      }
      if (!string.IsNullOrEmpty(RaygunSettings.Settings.IgnoreHeaderNames))
      {
        var ignoredNames = RaygunSettings.Settings.IgnoreHeaderNames.Split(',');
        IgnoreHeaderNames(ignoredNames);
      }
      if (!string.IsNullOrEmpty(RaygunSettings.Settings.IgnoreCookieNames))
      {
        var ignoredNames = RaygunSettings.Settings.IgnoreCookieNames.Split(',');
        IgnoreCookieNames(ignoredNames);
      }
      if (!string.IsNullOrEmpty(RaygunSettings.Settings.IgnoreServerVariableNames))
      {
        var ignoredNames = RaygunSettings.Settings.IgnoreServerVariableNames.Split(',');
        IgnoreServerVariableNames(ignoredNames);
      }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RaygunClientBase" /> class.
    /// Uses the ApiKey specified in the config file.
    /// </summary>
    public RaygunWebApiClient()
      : this(RaygunSettings.Settings.ApiKey)
    {
    }

    /// <summary>
    /// Causes Raygun4Net to listen for exceptions.
    /// </summary>
    /// <param name="config">The HttpConfiguration to attach to.</param>
    /// <param name="generateRaygunClient">An optional function to provide a custom RaygunWebApiClient instance to use for reporting exceptions.</param>
    public static void Attach(HttpConfiguration config, Func<RaygunWebApiClient> generateRaygunClient = null)
    {
      Detach(config);

      var entryAssembly = Assembly.GetCallingAssembly();
      string applicationVersion = entryAssembly.GetName().Version.ToString();

      var clientCreator = new RaygunWebApiClientProvider(generateRaygunClient, applicationVersion);

      config.Services.Add(typeof(IExceptionLogger), new RaygunWebApiExceptionLogger(clientCreator));

      _exceptionFilter = new RaygunWebApiExceptionFilter(clientCreator);
      config.Filters.Add(_exceptionFilter);

      _actionFilter = new RaygunWebApiActionFilter(clientCreator);
      config.Filters.Add(_actionFilter);

      var concreteActivator = config.Services.GetHttpControllerActivator();
      config.Services.Replace(typeof(IHttpControllerActivator), new RaygunWebApiControllerActivator(concreteActivator, clientCreator));

      var concreteControllerSelector = config.Services.GetHttpControllerSelector() ?? new DefaultHttpControllerSelector(config);
      config.Services.Replace(typeof(IHttpControllerSelector), new RaygunWebApiControllerSelector(concreteControllerSelector, clientCreator));

      var concreteActionSelector = config.Services.GetActionSelector() ?? new ApiControllerActionSelector();
      config.Services.Replace(typeof(IHttpActionSelector), new RaygunWebApiActionSelector(concreteActionSelector, clientCreator));
    }

    /// <summary>
    /// Causes Raygun4Net to stop listening for exceptions.
    /// </summary>
    /// <param name="config">The HttpConfiguration to detach from.</param>
    public static void Detach(HttpConfiguration config)
    {
      if (_exceptionFilter != null)
      {
        int exceptionLoggerIndex = config.Services.FindIndex(typeof(IExceptionLogger), (o) => o is RaygunWebApiExceptionLogger);
        if (exceptionLoggerIndex != -1)
        {
          config.Services.RemoveAt(typeof(IExceptionLogger), exceptionLoggerIndex);
        }

        config.Filters.Remove(_exceptionFilter);
        config.Filters.Remove(_actionFilter);

        RaygunWebApiControllerActivator controllerActivator = config.Services.GetHttpControllerActivator() as RaygunWebApiControllerActivator;
        if (controllerActivator != null)
        {
          config.Services.Replace(typeof(IHttpControllerActivator), controllerActivator.ConcreteActivator);
        }

        RaygunWebApiControllerSelector controllerSelector = config.Services.GetHttpControllerSelector() as RaygunWebApiControllerSelector;
        if (controllerSelector != null)
        {
          config.Services.Replace(typeof(IHttpControllerSelector), controllerSelector.ConcreteSelector);
        }

        RaygunWebApiActionSelector actionSelector = config.Services.GetActionSelector() as RaygunWebApiActionSelector;
        if (actionSelector != null)
        {
          config.Services.Replace(typeof(IHttpActionSelector), actionSelector.ConcreteSelector);
        }

        _exceptionFilter = null;
        _actionFilter = null;
      }
    }

    protected bool ValidateApiKey()
    {
      if (string.IsNullOrEmpty(_apiKey))
      {
        System.Diagnostics.Debug.WriteLine("ApiKey has not been provided, exception will not be logged");
        return false;
      }
      return true;
    }

    protected bool CanSend(RaygunMessage message)
    {
      if (message != null && message.Details != null && message.Details.Response != null)
      {
        return !RaygunSettings.Settings.ExcludedStatusCodes.Contains(message.Details.Response.StatusCode);
      }
      return true;
    }

    /// <summary>
    /// Gets or sets the user identity string.
    /// </summary>
    public string User { get; set; }

    /// <summary>
    /// Gets or sets information about the user including the identity string.
    /// </summary>
    public RaygunIdentifierMessage UserInfo { get; set; }

    /// <summary>
    /// Gets or sets the username/password credentials which are used to authenticate with the system default Proxy server, if one is set
    /// and requires credentials.
    /// </summary>
    public ICredentials ProxyCredentials { get; set; }

    /// <summary>
    /// Gets or sets a custom application version identifier for all error messages sent to the Raygun.io endpoint.
    /// </summary>
    public string ApplicationVersion { get; set; }

    /// <summary>
    /// Adds a list of outer exceptions that will be stripped, leaving only the valuable inner exception.
    /// This can be used when a wrapper exception, e.g. TargetInvocationException or HttpUnhandledException,
    /// contains the actual exception as the InnerException. The message and stack trace of the inner exception will then
    /// be used by Raygun for grouping and display. The above two do not need to be added manually,
    /// but if you have other wrapper exceptions that you want stripped you can pass them in here.
    /// </summary>
    /// <param name="wrapperExceptions">Exception types that you want removed and replaced with their inner exception.</param>
    public void AddWrapperExceptions(params Type[] wrapperExceptions)
    {
      foreach (Type wrapper in wrapperExceptions)
      {
        if (!_wrapperExceptions.Contains(wrapper))
        {
          _wrapperExceptions.Add(wrapper);
        }
      }
    }

    /// <summary>
    /// Specifies types of wrapper exceptions that Raygun should send rather than stripping out and sending the inner exception.
    /// This can be used to remove the default wrapper exceptions (TargetInvocationException and HttpUnhandledException).
    /// </summary>
    /// <param name="wrapperExceptions">Exception types that should no longer be stripped away.</param>
    public void RemoveWrapperExceptions(params Type[] wrapperExceptions)
    {
      foreach (Type wrapper in wrapperExceptions)
      {
        _wrapperExceptions.Remove(wrapper);
      }
    }

    /// <summary>
    /// Adds a list of keys to ignore when attaching the Form data of an HTTP POST request. This allows
    /// you to remove sensitive data from the transmitted copy of the Form on the HttpRequest by specifying the keys you want removed.
    /// This method is only effective in a web context.
    /// </summary>
    /// <param name="names">Keys to be stripped from the copy of the Form NameValueCollection when sending to Raygun.</param>
    public void IgnoreFormFieldNames(params string[] names)
    {
      _requestMessageOptions.AddFormFieldNames(names);
    }

    /// <summary>
    /// Adds a list of keys to ignore when attaching the headers of an HTTP POST request. This allows
    /// you to remove sensitive data from the transmitted copy of the Headers on the HttpRequest by specifying the keys you want removed.
    /// This method is only effective in a web context.
    /// </summary>
    /// <param name="names">Keys to be stripped from the copy of the Headers NameValueCollection when sending to Raygun.</param>
    public void IgnoreHeaderNames(params string[] names)
    {
      _requestMessageOptions.AddHeaderNames(names);
    }

    /// <summary>
    /// Adds a list of keys to ignore when attaching the cookies of an HTTP POST request. This allows
    /// you to remove sensitive data from the transmitted copy of the Cookies on the HttpRequest by specifying the keys you want removed.
    /// This method is only effective in a web context.
    /// </summary>
    /// <param name="names">Keys to be stripped from the copy of the Cookies NameValueCollection when sending to Raygun.</param>
    public void IgnoreCookieNames(params string[] names)
    {
      _requestMessageOptions.AddCookieNames(names);
    }

    /// <summary>
    /// Adds a list of keys to ignore when attaching the server variables of an HTTP POST request. This allows
    /// you to remove sensitive data from the transmitted copy of the ServerVariables on the HttpRequest by specifying the keys you want removed.
    /// This method is only effective in a web context.
    /// </summary>
    /// <param name="names">Keys to be stripped from the copy of the ServerVariables NameValueCollection when sending to Raygun.</param>
    public void IgnoreServerVariableNames(params string[] names)
    {
      _requestMessageOptions.AddServerVariableNames(names);
    }

    /// <summary>
    /// Transmits an exception to Raygun.io synchronously, using the version number of the originating assembly.
    /// </summary>
    /// <param name="exception">The exception to deliver.</param>
    public override void Send(Exception exception)
    {
      Send(exception, null, (IDictionary)null);
    }

    /// <summary>
    /// Transmits an exception to Raygun.io synchronously specifying a list of string tags associated
    /// with the message for identification. This uses the version number of the originating assembly.
    /// </summary>
    /// <param name="exception">The exception to deliver.</param>
    /// <param name="tags">A list of strings associated with the message.</param>
    public void Send(Exception exception, IList<string> tags)
    {
      Send(exception, tags, (IDictionary)null);
    }

    /// <summary>
    /// Transmits an exception to Raygun.io synchronously specifying a list of string tags associated
    /// with the message for identification, as well as sending a key-value collection of custom data.
    /// This uses the version number of the originating assembly.
    /// </summary>
    /// <param name="exception">The exception to deliver.</param>
    /// <param name="tags">A list of strings associated with the message.</param>
    /// <param name="userCustomData">A key-value collection of custom data that will be added to the payload.</param>
    public void Send(Exception exception, IList<string> tags, IDictionary userCustomData)
    {
      if (CanSend(exception))
      {
        _currentRequestMessage.Value = BuildRequestMessage();

        Send(BuildMessage(exception, tags, userCustomData));
        FlagAsSent(exception);
      }
    }

    /// <summary>
    /// Asynchronously transmits a message to Raygun.io.
    /// </summary>
    /// <param name="exception">The exception to deliver.</param>
    public void SendInBackground(Exception exception)
    {
      SendInBackground(exception, null, (IDictionary)null);
    }

    /// <summary>
    /// Asynchronously transmits an exception to Raygun.io.
    /// </summary>
    /// <param name="exception">The exception to deliver.</param>
    /// <param name="tags">A list of strings associated with the message.</param>
    public void SendInBackground(Exception exception, IList<string> tags)
    {
      SendInBackground(exception, tags, (IDictionary)null);
    }

    /// <summary>
    /// Asynchronously transmits an exception to Raygun.io.
    /// </summary>
    /// <param name="exception">The exception to deliver.</param>
    /// <param name="tags">A list of strings associated with the message.</param>
    /// <param name="userCustomData">A key-value collection of custom data that will be added to the payload.</param>
    public void SendInBackground(Exception exception, System.Collections.Generic.IList<string> tags, System.Collections.IDictionary userCustomData)
    {
      if (CanSend(exception))
      {
        // We need to process the HttpRequestMessage on the current thread,
        // otherwise it will be disposed while we are using it on the other thread.
        RaygunRequestMessage currentRequestMessage = BuildRequestMessage();

        ThreadPool.QueueUserWorkItem(c => {
          _currentRequestMessage.Value = currentRequestMessage;
          Send(BuildMessage(exception, tags, userCustomData));
        });
        FlagAsSent(exception);
      }
    }

    /// <summary>
    /// Asynchronously transmits a message to Raygun.io.
    /// </summary>
    /// <param name="raygunMessage">The RaygunMessage to send. This needs its OccurredOn property
    /// set to a valid DateTime and as much of the Details property as is available.</param>
    public void SendInBackground(RaygunMessage raygunMessage)
    {
      ThreadPool.QueueUserWorkItem(c => Send(raygunMessage));
    }

    internal void FlagExceptionAsSent(Exception exception)
    {
      base.FlagAsSent(exception);
    }

    private RaygunRequestMessage BuildRequestMessage()
    {
      RaygunRequestMessage message = _currentWebRequest.Value != null ? RaygunWebApiRequestMessageBuilder.Build(_currentWebRequest.Value, _requestMessageOptions) : null;
      _currentWebRequest.Value = null;
      return message;
    }

    internal RaygunWebApiClient CurrentHttpRequest(HttpRequestMessage request)
    {
      _currentWebRequest.Value = request;
      return this;
    }

    protected RaygunMessage BuildMessage(Exception exception, IList<string> tags, IDictionary userCustomData)
    {
      exception = StripWrapperExceptions(exception);

      var message = RaygunWebApiMessageBuilder.New
        .SetHttpDetails(_currentRequestMessage.Value)
        .SetEnvironmentDetails()
        .SetMachineName(Environment.MachineName)
        .SetExceptionDetails(exception)
        .SetClientDetails()
        .SetVersion(ApplicationVersion)
        .SetTags(tags)
        .SetUserCustomData(userCustomData)
        .SetUser(UserInfo ?? (!String.IsNullOrEmpty(User) ? new RaygunIdentifierMessage(User) : null))
        .Build();
      return message;
    }

    private Exception StripWrapperExceptions(Exception exception)
    {
      if (exception != null && _wrapperExceptions.Any(wrapperException => exception.GetType() == wrapperException && exception.InnerException != null))
      {
        return StripWrapperExceptions(exception.InnerException);
      }

      return exception;
    }

    /// <summary>
    /// Posts a RaygunMessage to the Raygun.io api endpoint.
    /// </summary>
    /// <param name="raygunMessage">The RaygunMessage to send. This needs its OccurredOn property
    /// set to a valid DateTime and as much of the Details property as is available.</param>
    public void Send(RaygunMessage raygunMessage)
    {
      if (ValidateApiKey())
      {
        bool canSend = OnSendingMessage(raygunMessage) && CanSend(raygunMessage);
        if (canSend)
        {
          using (var client = new WebClient())
          {
            client.Headers.Add("X-ApiKey", _apiKey);
            client.Encoding = System.Text.Encoding.UTF8;

            if (WebRequest.DefaultWebProxy != null)
            {
              Uri proxyUri = WebRequest.DefaultWebProxy.GetProxy(new Uri(RaygunSettings.Settings.ApiEndpoint.ToString()));

              if (proxyUri != null && proxyUri.AbsoluteUri != RaygunSettings.Settings.ApiEndpoint.ToString())
              {
                client.Proxy = new WebProxy(proxyUri, false);

                if (ProxyCredentials == null)
                {
                  client.UseDefaultCredentials = true;
                  client.Proxy.Credentials = CredentialCache.DefaultCredentials;
                }
                else
                {
                  client.UseDefaultCredentials = false;
                  client.Proxy.Credentials = ProxyCredentials;
                }
              }
            }

            try
            {
              var message = SimpleJson.SerializeObject(raygunMessage);
              client.UploadString(RaygunSettings.Settings.ApiEndpoint, message);
            }
            catch (Exception ex)
            {
              System.Diagnostics.Trace.WriteLine(string.Format("Error Logging Exception to Raygun.io {0}", ex.Message));

              if (RaygunSettings.Settings.ThrowOnError)
              {
                throw;
              }
            }
          }
        }
      }
    }
  }
}