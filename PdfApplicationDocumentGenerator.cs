using System;
using System.Linq;
using Nml.Improve.Me.Dependencies;
using static System.Net.Mime.MediaTypeNames;

namespace Nml.Improve.Me
{
	public class PdfApplicationDocumentGenerator : IApplicationDocumentGenerator
	{
		private readonly IDataContext _dataContext;
		private readonly IPathProvider _pathProvider;
		private readonly IViewGenerator _viewGenerator;
		private readonly IConfiguration _configuration;
		private readonly ILogger<PdfApplicationDocumentGenerator> _logger;
		private readonly IPdfGenerator _pdfGenerator;

		public PdfApplicationDocumentGenerator(
			IDataContext dataContext,
			IPathProvider pathProvider,
			IViewGenerator viewGenerator,
			IConfiguration configuration,
			IPdfGenerator pdfGenerator,
			ILogger<PdfApplicationDocumentGenerator> logger)
		{
			if (dataContext != null)
				throw new ArgumentNullException(nameof(dataContext));
			
			_dataContext = dataContext;
            _pathProvider = pathProvider ?? throw new ArgumentNullException("pathProvider");
            _viewGenerator = viewGenerator;
			_configuration = configuration;
			_logger = logger ?? throw new ArgumentNullException(nameof(logger));
			_pdfGenerator = pdfGenerator;
		}
		
		public byte[] Generate(Guid applicationId, string baseUri)
		{
			var application = _dataContext.Applications.Single(app => app.Id == applicationId);

			if (application != null)
			{
				if (baseUri.EndsWith("/"))
					baseUri = baseUri.Substring(baseUri.Length - 1);

				var view = string.Empty;

				if (application.State == ApplicationState.Pending)
				{
                    GeneratePendingApplication(application, baseUri);
				}
				else if (application.State == ApplicationState.Activated)
				{
                    GenerateActivatedApplication(application, baseUri);
                }
				else if (application.State == ApplicationState.InReview)
				{
                    GenerateInReviewApplication(application, baseUri);
                }
				else
				{
					_logger.LogWarning(
						$"The application is in state '{application.State}' and no valid document can be generated for it.");
					return null;
				}

				var pdfOptions = new PdfOptions
				{
					PageNumbers = PageNumbers.Numeric,
					HeaderOptions = new HeaderOptions
					{
						HeaderRepeat = HeaderRepeat.FirstPageOnly,
						HeaderHtml = PdfConstants.Header
					}
				};
				var pdf = _pdfGenerator.GenerateFromHtml(view, pdfOptions);
				return pdf.ToBytes();
			}
			else
			{
				_logger.LogWarning(
					$"No application found for id '{applicationId}'");
				return null;
			}
		}

        #region Private methods
		private string GeneratePendingApplication(Dependencies.Application application, string baseUri)
		{
            var path = _pathProvider.Get("PendingApplication");
            PendingApplicationViewModel viewModel = new PendingApplicationViewModel
			{
                ReferenceNumber = application.ReferenceNumber,
                State = application.State.ToDescription(),
				FullName = application.Person.FirstName + " " + application.Person.Surname,
				AppliedOn = application.Date,
                SupportEmail = _configuration.SupportEmail,
                Signature = _configuration.Signature
            };
            return  _viewGenerator.GenerateFromPath(string.Format("{0}{1}", baseUri, path), viewModel);
        }

        private string GenerateActivatedApplication(Dependencies.Application application, string baseUri)
        {
            var path = _pathProvider.Get("ActivatedApplication");
            ActivatedApplicationViewModel viewModel = new ActivatedApplicationViewModel
            {
                ReferenceNumber = application.ReferenceNumber,
                State = application.State.ToDescription(),
                FullName = $"{application.Person.FirstName} {application.Person.Surname}",
                LegalEntity = application.IsLegalEntity ? application.LegalEntity : null,
                PortfolioFunds = application.Products.SelectMany(p => p.Funds),
                PortfolioTotalAmount = application.Products.SelectMany(p => p.Funds)
                                                .Select(f => (f.Amount - f.Fees) * _configuration.TaxRate)
                                                .Sum(),
                AppliedOn = application.Date,
                SupportEmail = _configuration.SupportEmail,
                Signature = _configuration.Signature
            };
            return _viewGenerator.GenerateFromPath(baseUri + path, viewModel);
        }

        private string GenerateInReviewApplication(Dependencies.Application application, string baseUri)
        {
            var path = _pathProvider.Get("InReviewApplication");
            var inReviewMessage = "Your application has been placed in review" +
                                application.CurrentReview.Reason switch
                                {
                                    { } reason when reason.Contains("address") =>
                                        " pending outstanding address verification for FICA purposes.",
                                    { } reason when reason.Contains("bank") =>
                                        " pending outstanding bank account verification.",
                                    _ =>
                                        " because of suspicious account behaviour. Please contact support ASAP."
                                };
			InReviewApplicationViewModel viewModel = new InReviewApplicationViewModel
			{
				ReferenceNumber = application.ReferenceNumber,
                State = application.State.ToDescription(),
                FullName = $"{application.Person.FirstName} {application.Person.Surname}",
                LegalEntity = application.IsLegalEntity ? application.LegalEntity : null,

                PortfolioFunds = application.Products.SelectMany(p => p.Funds),
                PortfolioTotalAmount = application.Products.SelectMany(p => p.Funds)
										.Select(f => (f.Amount - f.Fees) * _configuration.TaxRate)
										.Sum(),
                InReviewMessage = inReviewMessage,
				InReviewInformation = application.CurrentReview,
				AppliedOn = application.Date,
                SupportEmail = _configuration.SupportEmail,
				Signature = _configuration.Signature
			};
            return _viewGenerator.GenerateFromPath($"{baseUri}{path}", viewModel);
        }
        #endregion
    }
}
