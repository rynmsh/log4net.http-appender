Log4Net Http Appender
===

Provides async logging to an http endpoint, log entries are queued and posted in a separate thread at the set
batch max value, to prevent overly chunky requests.

Thread sleeps after processing for 200ms then rechecks the queue.

context = HttpContext.Current when context for error log level

Logging event sent in batched collection to service url, the project key and environment values will be headers (X-ProjectKey, X-Environment)

```
{
	session_id: String,
	user: loggingEvent.Identity,
	logger: loggingEvent.LoggerName,
	level: loggingEvent.Level.Name.ToLower(),
	message: loggingEvent.RenderedMessage,
	stack_trace: loggingEvent.ExceptionObject.StackTrace,
	time_stamp: loggingEvent.TimeStamp.ToUniversalTime().ToString("u"),
	http: {
		url: context.Request.Url,
        url_referrer: context.Request.UrlReferrer,
        user_agent: context.Request.UserAgent,
        user_host_address: context.Request.UserHostAddress,
        user_host_name: context.Request.UserHostName,
        http_method: context.Request.HttpMethod,
        current_user: string.IsNullOrWhiteSpace(context.User.Identity.Name) ? null : context.User.Identity.Name,
        authentication_type: string.IsNullOrWhiteSpace(context.User.Identity.AuthenticationType) ? null : context.User.Identity.AuthenticationType,
        request_headers: [{
			key: String,
			value: String
		}],
		form_params: [{
			key: String,
			value: String
		}]
	}
}
```

Configuration
===

```
<appender name="HttpAppender" type="Log4Net.HttpAppender.AsyncBatchHttpAppender, Log4Net.HttpAppender">
  <threshold value="Debug" />

  <!-- Required: HTTP endpoint -->
  <ServiceUrl value="HTTP_ENDPOINT" />

  <!-- Required: Project/System ID/Name -->
  <ProjectKey value="FRIENDLY_DISPLAY_NAME" />

  <!-- Optional: Defaults to machine name -->
  <Environment value="dev|stage|prod" />

  <!-- Optional: Maximum number of events to submit per processing round. Default's to 40 -->
  <BatchMaxSize value="20" />

  <!-- Optional: Maximum number of events to submit per processing round. Default's to 40 -->
  <BatchSleepTime value="200" />

  <!-- Optional: Attach request headers and form values to http logging data. Default's to error,fatal,warn -->
  <LogHttpForLevels value="error,fatal,warn" />

  <layout type="log4net.Layout.PatternLayout">
    <param name="ConversionPattern" value="%date [%identity] %-5level %logger - %message%newline" />
  </layout>
</appender>
```

NuGet
===

https://www.nuget.org/packages/Log4Net.HttpAppender/
