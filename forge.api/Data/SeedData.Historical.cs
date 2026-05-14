using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Data.Context;
using Serilog;

namespace Forge.Api.Data;

public static partial class SeedData
{
    private static async Task SeedHistoricalDataAsync(
        AppDbContext db, int adminId, int akimId, int dhartId, int jsilvaId, int mreyesId,
        int pmorrisId, int lwilsonId, int cthompsonId, int bkellyId)
    {
        if (await db.Vendors.AnyAsync()) return;

        var seedsDir = Path.Combine(AppContext.BaseDirectory, "Data", "Seeds");
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        // ── user ref → DB id lookup ──────────────────────────────────────────
        var userIdMap = new Dictionary<string, int>
        {
            ["user:admin"]  = adminId,
            ["user:akim"]   = akimId,
            ["user:dhart"]  = dhartId,
            ["user:jsilva"] = jsilvaId,
            ["user:mreyes"] = mreyesId,
        };

        // ── 1. Vendors ───────────────────────────────────────────────────────
        var seedVendors = Deserialize<List<HVendor>>(
            Path.Combine(seedsDir, "historical-vendors.json"), opts);

        var vendorPairs = seedVendors.Select(sv => (sv.Id, Entity: new Vendor
        {
            CompanyName = sv.CompanyName, ContactName = sv.ContactName, Email = sv.Email,
            Phone = sv.Phone, Address = sv.Address, City = sv.City, State = sv.State,
            ZipCode = sv.ZipCode, Country = sv.Country, PaymentTerms = sv.PaymentTerms,
            IsActive = true, CreatedAt = D(sv.CreatedAt),
        })).ToList();

        db.Vendors.AddRange(vendorPairs.Select(p => p.Entity));
        await db.SaveChangesAsync();

        var vendorIds = vendorPairs.ToDictionary(p => p.Id, p => p.Entity.Id);
        Log.Information("Seeded {Count} vendors", vendorPairs.Count);

        // ── 2. Parts ─────────────────────────────────────────────────────────
        var seedParts = Deserialize<List<HPart>>(
            Path.Combine(seedsDir, "historical-parts.json"), opts);

        var partPairs = seedParts.Select(sp =>
        {
            // Pre-beta: the historical seed file still uses the legacy
            // single-axis partType naming ("Assembly" / "RawMaterial" / etc.).
            // Map it onto the three orthogonal axes the live schema requires.
            // The mapping mirrors the pre-beta legacy default-axis lookup that
            // PartWorkflowAdapter used to do at runtime — kept here as a
            // seed-time helper so the JSON itself can stay untouched.
            var (procurement, inventoryClass) = MapLegacyPartType(sp.PartType);
            return (sp.Id, Entity: new Part
            {
                // Phase-4 Name+Description split: the historical seed file still
                // calls the short identifier "description". Mirror it to Name so
                // the new schema's required field is populated, and leave the old
                // Description column null (it was always doing double duty as
                // name; new content goes through the API where the two are split).
                PartNumber = sp.PartNumber, Name = sp.Description, Description = null, Revision = sp.Revision,
                Status = Enum.Parse<PartStatus>(sp.Status),
                ProcurementSource = procurement,
                InventoryClass = inventoryClass,
                PreferredVendorId = vendorIds[sp.VendorRef],
                MinStockThreshold = sp.MinStockThreshold, ReorderPoint = sp.ReorderPoint,
                ReorderQuantity = sp.ReorderQuantity,
                SafetyStockDays = sp.SafetyStockDays, CreatedAt = D(sp.CreatedAt),
            });
        }).ToList();

        db.Parts.AddRange(partPairs.Select(p => p.Entity));
        await db.SaveChangesAsync();

        var partIds = partPairs.ToDictionary(p => p.Id, p => p.Entity.Id);
        Log.Information("Seeded {Count} parts", partPairs.Count);

        // Seed a preferred VendorPart row for each historical part — carries
        // the per-vendor lead time the JSON had under the legacy schema. OEM
        // identity / pricing tiers are left empty; the live UI will surface
        // them as "no manufacturer reported yet" / "no price configured".
        var vendorParts = seedParts
            .Where(sp => vendorIds.ContainsKey(sp.VendorRef))
            .Select(sp => new VendorPart
            {
                PartId = partIds[sp.Id],
                VendorId = vendorIds[sp.VendorRef],
                LeadTimeDays = sp.LeadTimeDays,
                IsApproved = true,
                IsPreferred = true,
            })
            .ToList();
        db.VendorParts.AddRange(vendorParts);
        await db.SaveChangesAsync();
        Log.Information("Seeded {Count} preferred vendor-parts", vendorParts.Count);

        // ── 3. Customers (new + existing extras) ─────────────────────────────
        var seedCustomers = Deserialize<HCustomersFile>(
            Path.Combine(seedsDir, "historical-customers.json"), opts);

        var customerIds   = new Dictionary<string, int>();
        var addressIds    = new Dictionary<string, int>(); // customerRef → default addr id

        // Insert new customers
        var newCustPairs = seedCustomers.NewCustomers.Select(sc => (sc.Id, Entity: new Customer
        {
            Name = sc.Name, CompanyName = sc.Name, Email = sc.Email, Phone = sc.Phone,
            IsActive = true, CreatedAt = D(sc.CreatedAt),
        })).ToList();

        db.Customers.AddRange(newCustPairs.Select(p => p.Entity));
        await db.SaveChangesAsync();

        foreach (var (id, entity) in newCustPairs)
            customerIds[id] = entity.Id;

        // Contacts for new customers
        foreach (var sc in seedCustomers.NewCustomers)
        {
            var custId = customerIds[sc.Id];
            foreach (var c in sc.Contacts)
            {
                db.Contacts.Add(new Contact
                {
                    CustomerId = custId, FirstName = c.FirstName, LastName = c.LastName,
                    Email = c.Email, Phone = c.Phone, IsPrimary = c.IsPrimary,
                    CreatedAt = D(c.CreatedAt),
                });
            }
        }

        // Addresses for new customers
        var newAddrPairs = seedCustomers.NewCustomers.Select(sc =>
        {
            var a = sc.Address;
            return (CustomerRef: sc.Id, Entity: new CustomerAddress
            {
                CustomerId = customerIds[sc.Id],
                Label = a.Label, AddressType = Enum.Parse<AddressType>(a.AddressType),
                Line1 = a.Line1, City = a.City, State = a.State,
                PostalCode = a.PostalCode, Country = a.Country,
                IsDefault = true,
                CreatedAt = a.CreatedAt != null ? D(a.CreatedAt) : D(sc.CreatedAt),
            });
        }).ToList();

        db.CustomerAddresses.AddRange(newAddrPairs.Select(p => p.Entity));

        // Existing customer extras
        var existingCustomers = await db.Customers.ToDictionaryAsync(c => c.Name);
        var extraAddrPairs = new List<(string CustomerRef, CustomerAddress Entity)>();

        foreach (var se in seedCustomers.ExistingCustomerExtras)
        {
            var cust = existingCustomers[se.LookupName];
            customerIds[se.Id] = cust.Id;

            foreach (var c in se.Contacts)
            {
                db.Contacts.Add(new Contact
                {
                    CustomerId = cust.Id, FirstName = c.FirstName, LastName = c.LastName,
                    Email = c.Email, Phone = c.Phone, IsPrimary = c.IsPrimary,
                    CreatedAt = D(c.CreatedAt),
                });
            }

            var a = se.Address;
            var addr = new CustomerAddress
            {
                CustomerId = cust.Id,
                Label = a.Label, AddressType = Enum.Parse<AddressType>(a.AddressType),
                Line1 = a.Line1, City = a.City, State = a.State,
                PostalCode = a.PostalCode, Country = a.Country,
                IsDefault = true,
                CreatedAt = a.CreatedAt != null ? D(a.CreatedAt) : DateTimeOffset.UtcNow,
            };
            extraAddrPairs.Add((se.Id, addr));
            db.CustomerAddresses.Add(addr);
        }

        await db.SaveChangesAsync();

        foreach (var (customerRef, entity) in newAddrPairs)
            addressIds[customerRef] = entity.Id;
        foreach (var (customerRef, entity) in extraAddrPairs)
            addressIds[customerRef] = entity.Id;

        Log.Information("Seeded {Count} new customers with contacts and addresses",
            seedCustomers.NewCustomers.Count);

        // ── 4. Leads ─────────────────────────────────────────────────────────
        var seedLeads = Deserialize<List<HLead>>(
            Path.Combine(seedsDir, "historical-leads.json"), opts);

        foreach (var sl in seedLeads)
        {
            var lead = new Lead
            {
                CompanyName = sl.CompanyName, ContactName = sl.ContactName,
                Email = sl.Email, Source = sl.Source,
                Status = Enum.Parse<LeadStatus>(sl.Status),
                Notes = sl.Notes,
                CreatedBy = userIdMap[sl.CreatedByRef],
                CreatedAt = D(sl.CreatedAt),
            };

            if (sl.ConvertedCustomerRef != null)
                lead.ConvertedCustomerId = customerIds[sl.ConvertedCustomerRef];
            if (sl.LostReason != null)
                lead.LostReason = sl.LostReason;
            if (sl.FollowUpDate != null)
                lead.FollowUpDate = D(sl.FollowUpDate);

            db.Leads.Add(lead);
        }

        await db.SaveChangesAsync();
        Log.Information("Seeded {Count} leads", seedLeads.Count);

        // ── 5. Production track + stages ─────────────────────────────────────
        var prodTrack = await db.TrackTypes.FirstAsync(t => t.Code == "production");
        var stages = await db.JobStages
            .Where(s => s.TrackTypeId == prodTrack.Id)
            .ToDictionaryAsync(s => s.Code);

        // ── 6. Order chains ──────────────────────────────────────────────────
        var seedChains = Deserialize<List<HChain>>(
            Path.Combine(seedsDir, "historical-chains.json"), opts);

        var solIds  = new Dictionary<string, int>(); // "$id" → SalesOrderLine.Id
        var jobNums = new Dictionary<string, int>(); // "job:J-XXXX" → Job.Id
        var histJobs = new List<Job>();               // for time entries (completed jobs only)

        int jobPos = 2000;

        foreach (var chain in seedChains)
        {
            var custId = customerIds[chain.CustomerRef];
            var addrId = addressIds[chain.CustomerRef];

            // Estimate (stored as Quote with Type=Estimate)
            Quote? estimate = null;
            if (chain.Estimate is { } se)
            {
                estimate = new Quote
                {
                    Type = QuoteType.Estimate,
                    CustomerId = custId,
                    Title = se.Title,
                    EstimatedAmount = se.EstimatedAmount,
                    Status = Enum.Parse<QuoteStatus>(se.Status),
                    AssignedToId = userIdMap[se.AssignedToRef],
                    ExpirationDate = D(se.ValidUntil),
                    ConvertedAt = D(se.ConvertedAt),
                    CreatedAt = D(se.CreatedAt),
                };
                db.Quotes.Add(estimate);
                await db.SaveChangesAsync();
            }

            // Quote
            Quote? quote = null;
            if (chain.Quote is { } sq)
            {
                quote = new Quote
                {
                    Type = QuoteType.Quote,
                    QuoteNumber = sq.QuoteNumber,
                    CustomerId = custId,
                    Status = Enum.Parse<QuoteStatus>(sq.Status),
                    SentDate = D(sq.SentDate),
                    ExpirationDate = D(sq.ExpirationDate),
                    AcceptedDate = sq.AcceptedDate != null ? D(sq.AcceptedDate) : null,
                    TaxRate = sq.TaxRate,
                    Notes = sq.Notes,
                    CreatedAt = D(sq.CreatedAt),
                    SourceEstimateId = estimate?.Id,
                };
                foreach (var ql in sq.Lines)
                {
                    quote.Lines.Add(new QuoteLine
                    {
                        PartId = ql.PartRef != null ? partIds[ql.PartRef] : null,
                        Description = ql.Description,
                        Quantity = ql.Quantity,
                        UnitPrice = ql.UnitPrice,
                    });
                }
                db.Quotes.Add(quote);
                if (estimate != null)
                    estimate.Status = QuoteStatus.ConvertedToQuote;
                await db.SaveChangesAsync();
            }

            // Sales Order
            var sso = chain.SalesOrder;
            var so = new SalesOrder
            {
                OrderNumber = sso.OrderNumber,
                CustomerId = custId,
                QuoteId = quote?.Id,
                ShippingAddressId = addrId,
                Status = Enum.Parse<SalesOrderStatus>(sso.Status),
                CreditTerms = Enum.Parse<CreditTerms>(sso.CreditTerms),
                ConfirmedDate = D(sso.ConfirmedDate),
                RequestedDeliveryDate = D(sso.RequestedDeliveryDate),
                CustomerPO = sso.CustomerPO,
                TaxRate = sso.TaxRate,
                Notes = sso.Notes,
                CreatedAt = D(sso.CreatedAt),
            };
            var soLineList = new List<(string? RefId, SalesOrderLine Line)>();
            foreach (var sl in sso.Lines)
            {
                var line = new SalesOrderLine
                {
                    PartId = sl.PartRef != null ? partIds[sl.PartRef] : null,
                    Description = sl.Description,
                    Quantity = sl.Quantity,
                    UnitPrice = sl.UnitPrice,
                };
                so.Lines.Add(line);
                soLineList.Add((sl.RefId, line));
            }
            db.SalesOrders.Add(so);
            await db.SaveChangesAsync();

            // Map SO line $ids → DB IDs (lines now have DB IDs after save)
            foreach (var (refId, line) in soLineList)
            {
                if (refId != null)
                    solIds[refId] = line.Id;
            }

            // Jobs
            var chainJobs = new List<Job>();
            foreach (var sj in chain.Jobs)
            {
                var job = new Job
                {
                    JobNumber = sj.JobNumber,
                    Title = sj.Title,
                    TrackTypeId = prodTrack.Id,
                    CurrentStageId = stages[sj.StageCode].Id,
                    AssigneeId = sj.AssigneeRef != null ? userIdMap[sj.AssigneeRef] : null,
                    CustomerId = custId,
                    DueDate = sj.DueDate != null ? D(sj.DueDate) : null,
                    Priority = sj.Priority != null
                        ? Enum.Parse<JobPriority>(sj.Priority)
                        : JobPriority.Normal,
                    BoardPosition = ++jobPos,
                    CreatedAt = D(sj.CreatedAt),
                };
                chainJobs.Add(job);
                db.Jobs.Add(job);
            }
            await db.SaveChangesAsync();

            foreach (var job in chainJobs)
            {
                jobNums[$"job:{job.JobNumber}"] = job.Id;
                // Only collect completed jobs for time entries (skip in_production)
                if (chain.Jobs.First(j => j.JobNumber == job.JobNumber).StageCode != "in_production")
                    histJobs.Add(job);
            }

            // Purchase Orders
            if (chain.PurchaseOrders is { Count: > 0 } pos)
            {
                foreach (var spo in pos)
                {
                    var po = new PurchaseOrder
                    {
                        PONumber = spo.PoNumber,
                        VendorId = vendorIds[spo.VendorRef],
                        JobId = jobNums[spo.JobRef],
                        Status = Enum.Parse<PurchaseOrderStatus>(spo.Status),
                        SubmittedDate = D(spo.SubmittedDate),
                        ExpectedDeliveryDate = D(spo.ExpectedDeliveryDate),
                        ReceivedDate = spo.ReceivedDate != null ? D(spo.ReceivedDate) : null,
                        Notes = spo.Notes,
                        CreatedAt = D(spo.CreatedAt),
                    };
                    foreach (var pl in spo.Lines)
                    {
                        po.Lines.Add(new PurchaseOrderLine
                        {
                            PartId = partIds[pl.PartRef!],
                            Description = pl.Description,
                            OrderedQuantity = pl.OrderedQuantity,
                            UnitPrice = pl.UnitPrice,
                        });
                    }
                    db.PurchaseOrders.Add(po);
                }
                await db.SaveChangesAsync();
            }

            // Lots
            if (chain.Lots is { Count: > 0 } lots)
            {
                foreach (var sl in lots)
                {
                    db.LotRecords.Add(new LotRecord
                    {
                        LotNumber = sl.LotNumber,
                        PartId = partIds[sl.PartRef],
                        JobId = jobNums[sl.JobRef],
                        Quantity = sl.Quantity,
                        Notes = sl.Notes,
                        CreatedAt = D(sl.CreatedAt),
                    });
                }
                // No immediate save; batched with returns below
            }

            // Customer Returns
            if (chain.Returns is { Count: > 0 } returns)
            {
                foreach (var sr in returns)
                {
                    db.CustomerReturns.Add(new CustomerReturn
                    {
                        ReturnNumber = sr.ReturnNumber,
                        CustomerId = custId,
                        OriginalJobId = jobNums[sr.JobRef],
                        Reason = sr.Reason ?? string.Empty,
                        Status = Enum.Parse<CustomerReturnStatus>(sr.Status),
                        ReturnDate = D(sr.ReturnDate),
                        InspectedById = userIdMap[sr.InspectedByRef],
                        InspectedAt = D(sr.InspectedAt),
                        InspectionNotes = sr.InspectionNotes,
                        Notes = sr.Notes,
                        CreatedAt = D(sr.CreatedAt),
                    });
                }
            }

            if ((chain.Lots?.Count ?? 0) + (chain.Returns?.Count ?? 0) > 0)
                await db.SaveChangesAsync();

            // Shipment
            Shipment? shipment = null;
            if (chain.Shipment is { } ss)
            {
                shipment = new Shipment
                {
                    ShipmentNumber = ss.ShipmentNumber,
                    SalesOrderId = so.Id,
                    ShippingAddressId = addrId,
                    Status = Enum.Parse<ShipmentStatus>(ss.Status),
                    Carrier = ss.Carrier,
                    TrackingNumber = ss.TrackingNumber,
                    ShippedDate = D(ss.ShippedDate),
                    DeliveredDate = ss.DeliveredDate != null ? D(ss.DeliveredDate) : null,
                    ShippingCost = ss.ShippingCost,
                    Notes = ss.Notes,
                    CreatedAt = D(ss.CreatedAt),
                };
                foreach (var sl in ss.Lines)
                {
                    shipment.Lines.Add(new ShipmentLine
                    {
                        SalesOrderLineId = solIds[sl.SoLineRef],
                        Quantity = sl.Quantity,
                    });
                }
                db.Shipments.Add(shipment);
                await db.SaveChangesAsync();
            }

            // Invoice
            Invoice? invoice = null;
            if (chain.Invoice is { } si)
            {
                invoice = new Invoice
                {
                    InvoiceNumber = si.InvoiceNumber,
                    CustomerId = custId,
                    SalesOrderId = so.Id,
                    ShipmentId = shipment?.Id,
                    Status = Enum.Parse<InvoiceStatus>(si.Status),
                    InvoiceDate = D(si.InvoiceDate),
                    DueDate = D(si.DueDate),
                    CreditTerms = Enum.Parse<CreditTerms>(si.CreditTerms),
                    TaxRate = si.TaxRate,
                    Notes = si.Notes,
                    CreatedAt = D(si.CreatedAt),
                };
                foreach (var il in si.Lines)
                {
                    invoice.Lines.Add(new InvoiceLine
                    {
                        Description = il.Description,
                        Quantity = il.Quantity,
                        UnitPrice = il.UnitPrice,
                    });
                }
                db.Invoices.Add(invoice);
                await db.SaveChangesAsync();
            }

            // Payment(s)
            var payments = new List<HPayment>();
            if (chain.Payment != null)  payments.Add(chain.Payment);
            if (chain.Payments != null) payments.AddRange(chain.Payments);

            foreach (var sp in payments)
            {
                var pmt = new Payment
                {
                    PaymentNumber = sp.PaymentNumber,
                    CustomerId = custId,
                    Method = Enum.Parse<PaymentMethod>(sp.Method),
                    Amount = sp.Amount,
                    PaymentDate = D(sp.PaymentDate),
                    ReferenceNumber = sp.ReferenceNumber,
                    Notes = sp.Notes,
                    CreatedAt = D(sp.CreatedAt),
                };
                if (invoice != null)
                    pmt.Applications.Add(new PaymentApplication { InvoiceId = invoice.Id, Amount = sp.Amount });
                db.Payments.Add(pmt);
            }
            if (payments.Count > 0)
                await db.SaveChangesAsync();

            Log.Debug("Seeded chain {ChainId}", chain.ChainId);
        }

        Log.Information("Seeded {Count} order chains", seedChains.Count);

        // ── 7. Historical time entries ────────────────────────────────────────
        var rng = new Random(42);
        var workerIds = new[] { akimId, dhartId, jsilvaId, mreyesId };
        var timeCategories = new[] { "CNC Setup", "Machining", "Deburr & Finish", "QC Inspection", "Assembly", "Programming", "Material Prep" };

        var timeEntries = new List<TimeEntry>();
        foreach (var job in histJobs)
        {
            int entryCount = rng.Next(2, 5);
            var baseDate = job.CreatedAt.AddDays(2);
            for (int i = 0; i < entryCount; i++)
            {
                var entryDate = DateOnly.FromDateTime(baseDate.AddDays(i * 2).DateTime);
                timeEntries.Add(new TimeEntry
                {
                    JobId = job.Id,
                    UserId = job.AssigneeId ?? workerIds[rng.Next(workerIds.Length)],
                    Date = entryDate,
                    DurationMinutes = rng.Next(90, 480),
                    Category = timeCategories[rng.Next(timeCategories.Length)],
                    Notes = $"Work on {job.JobNumber}",
                    IsManual = true,
                    IsLocked = true,
                    CreatedAt = new DateTimeOffset(entryDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                });
            }
        }
        db.TimeEntries.AddRange(timeEntries);
        await db.SaveChangesAsync();
        Log.Information("Seeded {Count} historical time entries across {Jobs} jobs",
            timeEntries.Count, histJobs.Count);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static DateTimeOffset D(string date) =>
        DateTimeOffset.Parse(date, null, System.Globalization.DateTimeStyles.AssumeUniversal);

    private static T Deserialize<T>(string path, JsonSerializerOptions opts) =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(path), opts)
        ?? throw new InvalidOperationException($"Failed to deserialize {path}");

    /// <summary>
    /// Pre-beta: maps the legacy single-axis <c>partType</c> string used by
    /// the historical seed JSON to the three orthogonal axes the live schema
    /// requires. The seed JSON predates the axis decomposition; rather than
    /// rewrite every row by hand we keep this helper to translate at load
    /// time. New code should never call this — produce axes directly.
    /// </summary>
    private static (ProcurementSource, InventoryClass) MapLegacyPartType(string legacy) => legacy switch
    {
        "Assembly" => (ProcurementSource.Make, InventoryClass.Subassembly),
        "RawMaterial" => (ProcurementSource.Buy, InventoryClass.Raw),
        "Consumable" => (ProcurementSource.Buy, InventoryClass.Consumable),
        "Tooling" => (ProcurementSource.Buy, InventoryClass.Tool),
        "Fastener" => (ProcurementSource.Buy, InventoryClass.Component),
        "Electronic" => (ProcurementSource.Buy, InventoryClass.Component),
        "Packaging" => (ProcurementSource.Buy, InventoryClass.Consumable),
        _ => (ProcurementSource.Buy, InventoryClass.Component), // "Part" catch-all
    };

    // ── Seed POCOs ───────────────────────────────────────────────────────────

    private sealed class HVendor
    {
        [JsonPropertyName("$id")] public string Id { get; set; } = "";
        public string CompanyName { get; set; } = "";
        public string ContactName { get; set; } = "";
        public string Email       { get; set; } = "";
        public string Phone       { get; set; } = "";
        public string Address     { get; set; } = "";
        public string City        { get; set; } = "";
        public string State       { get; set; } = "";
        public string ZipCode     { get; set; } = "";
        public string Country     { get; set; } = "";
        public string PaymentTerms { get; set; } = "";
        public string CreatedAt   { get; set; } = "";
    }

    private sealed class HPart
    {
        [JsonPropertyName("$id")] public string Id { get; set; } = "";
        public string PartNumber        { get; set; } = "";
        public string Description       { get; set; } = "";
        public string Revision          { get; set; } = "";
        public string Status            { get; set; } = "";
        public string PartType          { get; set; } = "";
        public string Material          { get; set; } = "";
        public string VendorRef         { get; set; } = "";
        public int    MinStockThreshold { get; set; }
        public int    ReorderPoint      { get; set; }
        public int    ReorderQuantity   { get; set; }
        public int    LeadTimeDays      { get; set; }
        public int    SafetyStockDays   { get; set; }
        public string CreatedAt         { get; set; } = "";
    }

    private sealed class HCustomersFile
    {
        public List<HNewCustomer>          NewCustomers          { get; set; } = [];
        public List<HExistingCustomerExtra> ExistingCustomerExtras { get; set; } = [];
    }

    private sealed class HNewCustomer
    {
        [JsonPropertyName("$id")] public string Id { get; set; } = "";
        public string       Name      { get; set; } = "";
        public string       Email     { get; set; } = "";
        public string       Phone     { get; set; } = "";
        public string       CreatedAt { get; set; } = "";
        public List<HContact> Contacts { get; set; } = [];
        public HAddress     Address   { get; set; } = new();
    }

    private sealed class HExistingCustomerExtra
    {
        [JsonPropertyName("$id")] public string Id { get; set; } = "";
        public string       LookupName { get; set; } = "";
        public List<HContact> Contacts { get; set; } = [];
        public HAddress     Address    { get; set; } = new();
    }

    private sealed class HContact
    {
        public string FirstName { get; set; } = "";
        public string LastName  { get; set; } = "";
        public string Email     { get; set; } = "";
        public string Phone     { get; set; } = "";
        public bool   IsPrimary { get; set; }
        public string CreatedAt { get; set; } = "";
    }

    private sealed class HAddress
    {
        public string  Label       { get; set; } = "";
        public string  AddressType { get; set; } = "";
        public string  Line1       { get; set; } = "";
        public string  City        { get; set; } = "";
        public string  State       { get; set; } = "";
        public string  PostalCode  { get; set; } = "";
        public string  Country     { get; set; } = "";
        public string? CreatedAt   { get; set; }
    }

    private sealed class HLead
    {
        public string  CompanyName         { get; set; } = "";
        public string  ContactName         { get; set; } = "";
        public string  Email               { get; set; } = "";
        public string  Source              { get; set; } = "";
        public string  Status              { get; set; } = "";
        public string  Notes               { get; set; } = "";
        public string? ConvertedCustomerRef { get; set; }
        public string? LostReason          { get; set; }
        public string? FollowUpDate        { get; set; }
        public string  CreatedByRef        { get; set; } = "";
        public string  CreatedAt           { get; set; } = "";
    }

    private sealed class HChain
    {
        public string            ChainId       { get; set; } = "";
        public string            CustomerRef   { get; set; } = "";
        public HEstimate?        Estimate      { get; set; }
        public HQuote?           Quote         { get; set; }
        public HSalesOrder       SalesOrder    { get; set; } = new();
        public List<HJob>        Jobs          { get; set; } = [];
        public List<HPO>?        PurchaseOrders { get; set; }
        public List<HLot>?       Lots          { get; set; }
        public HShipment?        Shipment      { get; set; }
        public HInvoice?         Invoice       { get; set; }
        public HPayment?         Payment       { get; set; }
        public List<HPayment>?   Payments      { get; set; }
        public List<HReturn>?    Returns       { get; set; }
    }

    private sealed class HEstimate
    {
        public string  Title           { get; set; } = "";
        public decimal EstimatedAmount { get; set; }
        public string  Status          { get; set; } = "";
        public string  AssignedToRef   { get; set; } = "";
        public string  ValidUntil      { get; set; } = "";
        public string  ConvertedAt     { get; set; } = "";
        public string  CreatedAt       { get; set; } = "";
    }

    private sealed class HQuote
    {
        public string       QuoteNumber    { get; set; } = "";
        public string       Status         { get; set; } = "";
        public string       SentDate       { get; set; } = "";
        public string       ExpirationDate { get; set; } = "";
        public string?      AcceptedDate   { get; set; }
        public decimal      TaxRate        { get; set; }
        public string?      Notes          { get; set; }
        public string       CreatedAt      { get; set; } = "";
        public List<HQuoteLine> Lines      { get; set; } = [];
    }

    private sealed class HQuoteLine
    {
        public string?  PartRef     { get; set; }
        public string   Description { get; set; } = "";
        public int      Quantity    { get; set; }
        public decimal  UnitPrice   { get; set; }
    }

    private sealed class HSalesOrder
    {
        public string          OrderNumber           { get; set; } = "";
        public string          Status                { get; set; } = "";
        public string          CreditTerms           { get; set; } = "";
        public string          ConfirmedDate         { get; set; } = "";
        public string          RequestedDeliveryDate { get; set; } = "";
        public string?         CustomerPO            { get; set; }
        public decimal         TaxRate               { get; set; }
        public string?         Notes                 { get; set; }
        public string          CreatedAt             { get; set; } = "";
        public List<HSOLine>   Lines                 { get; set; } = [];
    }

    private sealed class HSOLine
    {
        [JsonPropertyName("$id")] public string? RefId { get; set; }
        public string?  PartRef     { get; set; }
        public string   Description { get; set; } = "";
        public int      Quantity    { get; set; }
        public decimal  UnitPrice   { get; set; }
    }

    private sealed class HJob
    {
        public string  JobNumber   { get; set; } = "";
        public string  Title       { get; set; } = "";
        public string  StageCode   { get; set; } = "";
        public string? AssigneeRef { get; set; }
        public string  CreatedAt   { get; set; } = "";
        public string? DueDate     { get; set; }
        public string? Priority    { get; set; }
    }

    private sealed class HPO
    {
        public string    PoNumber            { get; set; } = "";
        public string    VendorRef           { get; set; } = "";
        public string    JobRef              { get; set; } = "";
        public string    Status              { get; set; } = "";
        public string    SubmittedDate       { get; set; } = "";
        public string    ExpectedDeliveryDate { get; set; } = "";
        public string?   ReceivedDate        { get; set; }
        public string?   Notes               { get; set; }
        public string    CreatedAt           { get; set; } = "";
        public List<HPOLine> Lines           { get; set; } = [];
    }

    private sealed class HPOLine
    {
        public string?  PartRef         { get; set; }
        public string   Description     { get; set; } = "";
        public int      OrderedQuantity { get; set; }
        public decimal  UnitPrice       { get; set; }
    }

    private sealed class HLot
    {
        public string  LotNumber { get; set; } = "";
        public string  PartRef   { get; set; } = "";
        public string  JobRef    { get; set; } = "";
        public int     Quantity  { get; set; }
        public string? Notes     { get; set; }
        public string  CreatedAt { get; set; } = "";
    }

    private sealed class HShipment
    {
        public string          ShipmentNumber { get; set; } = "";
        public string          Status         { get; set; } = "";
        public string          Carrier        { get; set; } = "";
        public string          TrackingNumber { get; set; } = "";
        public string          ShippedDate    { get; set; } = "";
        public string?         DeliveredDate  { get; set; }
        public decimal         ShippingCost   { get; set; }
        public string?         Notes          { get; set; }
        public string          CreatedAt      { get; set; } = "";
        public List<HShipLine> Lines          { get; set; } = [];
    }

    private sealed class HShipLine
    {
        public string SoLineRef { get; set; } = "";
        public int    Quantity  { get; set; }
    }

    private sealed class HInvoice
    {
        public string          InvoiceNumber { get; set; } = "";
        public string          Status        { get; set; } = "";
        public string          InvoiceDate   { get; set; } = "";
        public string          DueDate       { get; set; } = "";
        public string          CreditTerms   { get; set; } = "";
        public decimal         TaxRate       { get; set; }
        public string?         Notes         { get; set; }
        public string          CreatedAt     { get; set; } = "";
        public List<HInvLine>  Lines         { get; set; } = [];
    }

    private sealed class HInvLine
    {
        public string  Description { get; set; } = "";
        public int     Quantity    { get; set; }
        public decimal UnitPrice   { get; set; }
    }

    private sealed class HPayment
    {
        public string  PaymentNumber   { get; set; } = "";
        public string  Method          { get; set; } = "";
        public decimal Amount          { get; set; }
        public string  PaymentDate     { get; set; } = "";
        public string? ReferenceNumber { get; set; }
        public string? Notes           { get; set; }
        public string  CreatedAt       { get; set; } = "";
    }

    private sealed class HReturn
    {
        public string  ReturnNumber    { get; set; } = "";
        public string  JobRef          { get; set; } = "";
        public string? Reason          { get; set; }
        public string  Status          { get; set; } = "";
        public string  ReturnDate      { get; set; } = "";
        public string  InspectedByRef  { get; set; } = "";
        public string  InspectedAt     { get; set; } = "";
        public string? InspectionNotes { get; set; }
        public string? Notes           { get; set; }
        public string  CreatedAt       { get; set; } = "";
    }
}
