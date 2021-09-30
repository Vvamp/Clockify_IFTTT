using System.Reflection.Metadata;
using System.Xml;
using System;
using Clockify.Net;
using Clockify.Net.Models.Workspaces;
using Clockify.Net.Models.Users;
using Clockify.Net.Models.TimeEntries;
using Clockify.Net.Models.Projects;
using System.Threading.Tasks;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Linq;
using System.Net;
using System.Threading;
namespace Clockify_IFTTT
{


    class Program
    {


        public static string description = null;



        static async Task Main(string[] args)
        {
            if (args.Length <= 0)
            {
                Logger.fatal("No api key supplied. Please pass your clockify api key");
                return;
            }

            string api_key = args[0];

            Logger.info("Initializing clockify client...");
            ClockifyClient clockify;
            try
            {
                clockify = new ClockifyClient(api_key);
            }
            catch (Exception e)
            {
                Logger.fatal("Failed to initialize clockify client. Did you pass the right api key?");
                Logger.fatal("Error: " + e.Message);
                return;
            }

            Logger.info("Getting Current Workspace...");
            List<WorkspaceDto> workspaces = JsonConvert.DeserializeObject<List<WorkspaceDto>>((await clockify.GetWorkspacesAsync()).Content);
            WorkspaceDto currentWorkspace = workspaces.First(x => x.Name == "OK System Development");

            Logger.info("Loading Current User...");
            UserDto currentUser = JsonConvert.DeserializeObject<UserDto>((await clockify.GetCurrentUserAsync()).Content);

            Logger.info("Loading Default Project...");
            List<ProjectDtoImpl> projects = JsonConvert.DeserializeObject<List<ProjectDtoImpl>>((await clockify.FindAllProjectsOnWorkspaceAsync(currentWorkspace.Id)).Content);
            ProjectDtoImpl currentProject = projects.First(x => x.Name == "Database automatiseringsproject");

            Logger.info("Initialization Complete.");


            // Tests
            // Logger.info("Starting test timer...");
            // await startEntry(clockify, currentWorkspace, currentProject, currentUser, "I am a test. Don't count me!");
            // Logger.info("Starting second test timer(should not be created!)...");
            // await startEntry(clockify, currentWorkspace, currentProject, currentUser, "I am a test. I should not exist!");

            // await Task.Delay(60000);
            // Logger.info("Stopping test timer(1 minute(s) passed)...");
            // await stopEntry(clockify, currentWorkspace, currentUser);
            // Logger.info("Printing All Time Entries...");
            // await printTimeEntries(clockify, currentUser, currentWorkspace);


            // Start a weblistener on port 8080
            HttpListener listener = new HttpListener();
            listener.Prefixes.Add("http://*:8080/");
            listener.Start();


            while (true) // Loop until hell freezes over
            {
                // Wait until our url is called
                HttpListenerContext ctx = listener.GetContext();

                description = ""; // make sure to clear the description, so I don't use an old description when I give none

                // Skip if only the base url is supplied
                if (ctx.Request.Url.Segments.Length < 2)
                {
                    continue;
                }

                // Find the action on part 1 of the url
                string methodName = ctx.Request.Url.Segments[1].Replace("/", "");

                // Act based on function
                switch (methodName)
                {
                    case "start":
                        Logger.info("Received http request to start");
                        if (ctx.Request.Url.Segments.Length >= 3)
                        {
                            string desc = ctx.Request.Url.Segments[2];
                            desc = desc.Replace("_", " ");
                            Logger.info("Start description: '" + desc + "'");
                            description = desc;
                        }
                        await startEntry(clockify, currentWorkspace, currentProject, currentUser, description);

                        break;
                    case "stop":
                        Logger.info("Received http request to stop");
                        await stopEntry(clockify, currentWorkspace, currentUser);
                        break;

                    case "toggle":
                        Logger.info("Received http request to toggle");
                        if (ctx.Request.Url.Segments.Length >= 3)
                        {
                            string desc = ctx.Request.Url.Segments[2];
                            desc = desc.Replace("_", " ");
                            Logger.info("Toggle description: '" + desc + "'");
                            description = desc;
                        }
                        await toggle(clockify, currentWorkspace, currentProject, currentUser, description);
                        break;
                    case "status":
                        Logger.info("Received http request to return status");

                        bool isRunning = getActiveTimeEntry(clockify, currentWorkspace, currentUser) != null;
                        HttpListenerContext context = listener.GetContext();
                        HttpListenerRequest request = context.Request;
                        HttpListenerResponse response = context.Response;
                        string responseString = $"{isRunning.ToString()}";
                        byte[] buffer = System.Text.Encoding.UTF8.GetBytes(responseString);
                        response.ContentLength64 = buffer.Length;
                        System.IO.Stream output = response.OutputStream;
                        output.Write(buffer, 0, buffer.Length);
                        // You must close the output stream.
                        output.Close();


                        break;

                    default:
                        Logger.info("Base url called with no method. Ignoring...");
                        break;
                }


            }
        }

        // Print all time entries ever made by currentUser
        static async Task printTimeEntries(ClockifyClient client, UserDto currentUser, WorkspaceDto currentWorkspace)
        {
            var entry_rest_response = await client.FindAllTimeEntriesForUserAsync(currentWorkspace.Id, currentUser.ID);
            var entry_rest_data = entry_rest_response.Content;
            List<TimeEntryDtoImpl> timeEntries = JsonConvert.DeserializeObject<List<TimeEntryDtoImpl>>(entry_rest_data);
            Logger.info($"Time Entries for {currentUser.Name}:");
            foreach (TimeEntryDtoImpl timeEntry in timeEntries)
            {
                Logger.info($" -{timeEntry.Description}");

            }
        }

        private static TimeEntryDtoImpl cachedTimeEntry = null; // The last time entry that was active

        // Load active time entry and cache it
        private static TimeEntryDtoImpl getActiveTimeEntry(ClockifyClient client, WorkspaceDto currentWorkspace, UserDto currentUser)
        {
            Logger.info("Checking active timer entry...");
            var entry_rest_response = client.FindAllTimeEntriesForUserAsync(currentWorkspace.Id, currentUser.ID, inProgress: true).Result;
            var entry_rest_data = entry_rest_response.Content;
            List<TimeEntryDtoImpl> entries = JsonConvert.DeserializeObject<List<TimeEntryDtoImpl>>(entry_rest_data);
            if (entries.Count < 1)
            {
                Logger.info("No active entries found");
                return null;
            }

            Logger.info($"Active entry found: {entries[0].Id}");
            cachedTimeEntry = entries[0];
            return entries[0];
        }

        // Start timer if possible
        static async Task startEntry(ClockifyClient client, WorkspaceDto currentWorkspace, ProjectDtoImpl currentProject, UserDto currentUser, string? description)
        {
            Logger.info("Received entry start request");
            // Check if no time is running yet
            if (getActiveTimeEntry(client, currentWorkspace, currentUser) != null)
            {
                Logger.info("Can't start timer: timer already running");
                return;
            }


            TimeEntryRequest ter = new TimeEntryRequest();
            ter.Billable = true;
            ter.Start = DateTime.Now.ToUniversalTime();
            ter.ProjectId = currentProject.Id;
            if (description != null)
            {
                ter.Description = description;
            }

            Logger.info("Starting timer entry...");
            await client.CreateTimeEntryAsync(currentWorkspace.Id, ter);
            Logger.info("Timer started successfully!");
        }

        // Toggle timer on/off
        static async Task toggle(ClockifyClient client, WorkspaceDto currentWorkspace, ProjectDtoImpl currentProject, UserDto currentUser, string? description)
        {
            Logger.info("Received entry toggle request");
            // Check if timer exists
            if (getActiveTimeEntry(client, currentWorkspace, currentUser) != null)
            {
                Logger.info("Timer exists, so toggling OFF");
                await stopEntry(client, currentWorkspace, currentUser);
                return;
            }
            else
            {
                Logger.info("No timer exists, so toggling ON");
                await startEntry(client, currentWorkspace, currentProject, currentUser, description);
                return;
            }

        }
        // Stop timer if possible
        static async Task stopEntry(ClockifyClient client, WorkspaceDto currentWorkspace, UserDto currentUser)
        {
            Logger.info("Received entry stop request");


            // Check if timer exists
            if (getActiveTimeEntry(client, currentWorkspace, currentUser) == null)
            {
                Logger.info("Can't stop timer: no timer exists");
                return;
            }

            if (cachedTimeEntry == null)
            {
                Logger.info("Timer might be running, but no timer is cached! Cancelling stop request...");
                return;
            }

            // Make an update request. We need to copy over the existing values(which is horribly stupid). The api's update is just an overwrite
            UpdateTimeEntryRequest uter = new UpdateTimeEntryRequest();
            uter.End = DateTime.Now.ToUniversalTime();
            uter.Start = cachedTimeEntry.TimeInterval.Start;
            uter.Billable = cachedTimeEntry.Billable;
            uter.Description = cachedTimeEntry.Description;
            uter.ProjectId = cachedTimeEntry.ProjectId;
            uter.TagIds = cachedTimeEntry.TagIds;

            Logger.info("Stopping timer...");
            await client.UpdateTimeEntryAsync(currentWorkspace.Id, cachedTimeEntry.Id, uter);
            Logger.info("Timer stopped successfully");


        }



    }
}
