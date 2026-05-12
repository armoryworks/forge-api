namespace Forge.Api.Data;

/// <summary>
/// Official PDF download URLs for state withholding forms.
/// Used by the PDF extraction pipeline to auto-download and extract form definitions.
/// URLs may change when states publish new revisions — update here when discovered.
/// </summary>
public static class StateWithholdingUrls
{
    public static Dictionary<string, string> GetAll() => new()
    {
        ["AL"] = "https://www.revenue.alabama.gov/wp-content/uploads/2025/04/A4_0425.pdf",
        ["AZ"] = "https://azdor.gov/sites/default/files/document/FORMS_WITHHOLDING_2025_A-4_f.pdf",
        ["AR"] = "https://www.dfa.arkansas.gov/wp-content/uploads/AR4EC2025_FI.pdf",
        ["CA"] = "https://edd.ca.gov/siteassets/files/pdf_pub_ctr/de4.pdf",
        ["CT"] = "https://portal.ct.gov/-/media/drs/forms/2024/withholdingforms/ct-w4_1224.pdf",
        ["DC"] = "https://otr.cfo.dc.gov/sites/default/files/dc/sites/otr/publication/attachments/2025_D-4_Fill_In.pdf",
        ["DE"] = "https://revenuefiles.delaware.gov/2021/DE-W4.pdf",
        ["GA"] = "https://dor.georgia.gov/document/form/tsdemployeeswithholdingallowancecertificateg-4pdf/download",
        ["HI"] = "https://files.hawaii.gov/tax/forms/current/hw4_i.pdf",
        ["IA"] = "https://revenue.iowa.gov/media/4324/download?inline",
        ["ID"] = "https://tax.idaho.gov/wp-content/uploads/forms/EFO00307/EFO00307_04-28-2025.pdf",
        ["IL"] = "https://tax.illinois.gov/content/dam/soi/en/web/tax/forms/withholding/documents/currentyear/il-w-4.pdf",
        ["IN"] = "https://forms.in.gov/Download.aspx?id=2702",
        ["KS"] = "https://www.ksrevenue.gov/pdf/k-4.pdf",
        ["KY"] = "https://revenue.ky.gov/Forms/42A804%20(K-4)%20(2026).pdf",
        ["LA"] = "https://dam.ldr.la.gov/taxforms/1300-1-26-F.pdf",
        ["MA"] = "https://www.mass.gov/doc/form-m-4-massachusetts-employees-withholding-exemption-certificate/download",
        ["MD"] = "https://www.marylandcomptroller.gov/content/dam/mdcomp/tax/forms/2025/MW507.pdf",
        ["ME"] = "https://www.maine.gov/revenue/sites/maine.gov.revenue/files/inline-files/26_form_w-4me_fillable.pdf",
        ["MI"] = "https://www.michigan.gov/-/media/Project/Websites/taxes/Forms/All-Years/MIW4.pdf",
        ["MN"] = "https://www.revenue.state.mn.us/sites/default/files/2025-12/w-4mn.pdf",
        ["MO"] = "https://dor.mo.gov/forms/MO%20W-4.pdf",
        ["MS"] = "https://www.dor.ms.gov/sites/default/files/tax-forms/business/89350258.pdf",
        ["NC"] = "https://www.ncdor.gov/tax-forms/nc-4-employees-withholding-allowance-certificate/open",
        ["NE"] = "https://revenue.nebraska.gov/sites/default/files/doc/tax-forms/w4n.pdf",
        ["NJ"] = "https://www.nj.gov/treasury/taxation/pdf/current/njw4.pdf",
        ["NY"] = "https://www.tax.ny.gov/pdf/current_forms/it/it2104_fill_in.pdf",
        ["OH"] = "https://tax.ohio.gov/documents/forms/employer_withholding/Generic/WTH_IT4.pdf",
        ["OK"] = "https://oklahoma.gov/content/dam/ok/en/tax/documents/forms/businesses/general/OK-W-4.pdf",
        ["OR"] = "https://www.oregon.gov/dor/forms/FormsPubs/form-or-w-4_101-402_2024.pdf",
        ["PA"] = "https://www.revenue.pa.gov/FormsandPublications/FormsforIndividuals/PIT/Documents/rev-419.pdf",
        ["RI"] = "https://tax.ri.gov/sites/g/files/xkgbur541/files/2026-01/RI%20W-4%202026.pdf",
        ["SC"] = "https://dor.sc.gov/sites/dor/files/forms/SCW4_2026.pdf",
        ["VA"] = "https://www.tax.virginia.gov/sites/default/files/taxforms/withholding/any/va-4-any.pdf",
        ["VT"] = "https://tax.vermont.gov/sites/tax/files/documents/W-4VT.pdf",
        ["WI"] = "https://www.revenue.wi.gov/DOR%20Publications/pb166.pdf",
        ["WV"] = "https://tax.wv.gov/Documents/Withholding/it104.pdf",
    };
}
