# v1.0.2 source contracts

Checked 2026-10-11 against the official USAC dataset metadata endpoints below.
This records the mappings used by the implementation, not a guarantee of real-time
publication or completeness relative to EPC.

| Source | Fields/behavior used | Limits |
| --- | --- | --- |
| [Form 470 metadata](https://opendata.usac.org/api/views/jt8s-3q52.json) | `application_number`, `billed_entity_number`, `funding_year`, `fcc_form_470_status`, `form_version`, `certified_date_time`, `last_modified_date_time`, services, PDF, and `allowable_contract_date`. | Query retains `fcc_form_470_status='Certified'`; multiple service/attachment rows collapse to one application. Disappearance does not establish a status transition. |
| [Form 471 metadata](https://opendata.usac.org/api/views/9s6i-myen.json) | `epc_organization_id`, `funding_year`, `application_number`, `form_471_status_name`, `form_version`, `certified_datetime`, services, PDF, `funding_request_amount`, `pre_discount_eligible_amount`. | Query requires a certification timestamp; Current rows take precedence over Original. Requested amounts are application-level totals across FRNs, not committed or disbursed amounts. |
| [USAC filing-window announcements](https://www.usac.org/e-rate/resources/announcements/) | Existing funding-year-specific filing-window parser and cached verified dates. | Missing or changed markup produces Unknown; dates do not assert exact daily cutoff times. |

The Form 470 metadata defines `allowable_contract_date` as a calendar date. The
portfolio displays its date component and source observation time. It does not
calculate a new ACD from a shortened certification date. USAC's
[competitive-bidding guidance](https://www.usac.org/e-rate/applicant-process/competitive-bidding/)
explains that material changes can require a restarted waiting period; users must
check current EPC/RFP information before acting on a saved date.

The Form 471 `has_service_provider_listed` field concerns assistance locating the
applicant's non-discount share; it is not a service-provider identity or a basis
for provider market-share comparisons. Such comparisons remain unavailable.

Queries request 1,000 rows per page and fail at the existing 50,000-row guard.
Ordering follows the existing application/service/version/modified keys. The
published API does not provide an atomic snapshot spanning all pages; results
are described as observations. Only a complete forced-live lookup can update the
portfolio baseline. Cached pages, failures, and cancellations cannot do so.

Application identity includes BEN, year, form, and application number. The shared
collapse step prefers Current rows and combines distinct services, avoiding sums
over duplicate service rows. Form 471 requested-funding totals use each observed
application once and become Unknown if any observed application's amount is
missing or invalid, or the query failed. They do not substitute for authoritative
funding-request or commitment records.
