
# Magic Library, a simplified NuGet wrapper for Magic's packages

[![Build status](https://travis-ci.org/polterguy/magic.library.svg?master)](https://travis-ci.org/polterguy/magic.library)

Helper project for [Magic](https://github.com/polterguy/magic), to wire up everything and initialize Magic.

This project will help you to wire up everything related to Magic. Normally you'd use it simply like the following from
your startup class in your .Net Core Web API project.

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using magic.library;

namespace your.app
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        IConfiguration Configuration { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton(Configuration);
            services.AddMvc().AddNewtonsoftJson();

            /*
             * Initializing Magic.
             * Notice, must be done AFTER you invoke "AddMvc".
             */
            services.AddMagic(Configuration, Configuration["magic:license"]);
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            /*
             * Initializing Magic.
             * Notice, must be done BEFORE you invoke "UseEndpoints".
             */
            app.UseMagic(Configuration);

            app.UseHttpsRedirection();
            app.UseCors(x => x.AllowAnyHeader().AllowAnyOrigin().AllowAnyMethod());
            app.UseAuthentication();
            app.UseRouting();
            app.UseEndpoints(conf => conf.MapControllers());
        }
    }
}

```

However, you can also take more control over how things are actually wired up, by instead of using the
_"do all methods"_ called `AddMagic` and `UseMagic`, invoke some of the specialized initialization methods,
you can find below.

* `IServiceCollection.AddMagicHttp`
* `IServiceCollection.AddMagicLog4netServices`
* `IServiceCollection.AddMagicSignals`
* `IServiceCollection.AddMagicEndpoints`
* `IServiceCollection.AddMagicFileServices`
* `IServiceCollection.AddMagicAuthorization`
* `IServiceCollection.AddMagicScheduler`
* `IServiceCollection.AddMagicMail`

The above methods is basically what the `AddMagic` method actually does, and they're extension methods of
`IServiceCollection`, that can be found in the `magic.library` namespace. Similar alternatives to `UseMagic` can
be found below.

* `IApplicationBuilder.UseMagicExceptions`
* `IApplicationBuilder.UseMagicStartupFiles`

If you use these methods instead of the _"do all methods"_, probably a large portion of your motivation would
be to _replace_ one of these methods with your own implementation, to exchange the default wiring up, by (for instance)
using a _"virtual database based file system"_ by creating your own service implementation of for instance `IFileService`
from _"magic.lambda.io"_, or use a different logging provider than the default, which is log4net, etc. If you wish
to do this, you'd probably benefit from looking at what the default implementation of your method does, to understand the
requirements from your method.

Doing this is very powerful, and allows you to change the way the system behaves by default - But is also definitely
considered an _"advanced exercise"_.

## License

Although most of Magic's source code is Open Source, you will need a license key to use it.
[You can obtain a license key here](https://servergardens.com/buy/).
Notice, 7 days after you put Magic into production, it will stop working, unless you have a valid
license for it.

* [Get licensed](https://servergardens.com/buy/)
