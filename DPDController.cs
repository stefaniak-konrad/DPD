using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using MSL.DataAccess.Contract.POCO;
using MSL.DataAccess.Contract.Repositories;
using Serilog;
using WebApi.DTO;
using DPDClient;
using packageOpenUMLFeV1 = DPDClient.packageOpenUMLFeV1;

namespace WebApi.Controllers
{
    [Produces("application/json")]
    [Route("api/[controller]/[action]")]
    public class DPDController : Controller
    {
        public IShippingRepository ShippingRepository { get; }
        public IShippingPackageRepository ShippingPackageRepository { get; }
        public ILocationsRepository LocationsRepository { get; }

        public DPDController(IShippingRepository shippingRepository, IShippingPackageRepository shippingPackageRepository, ILocationsRepository locationsRepository)
        {
            ShippingRepository = shippingRepository;
            ShippingPackageRepository = shippingPackageRepository;
            LocationsRepository = locationsRepository;
        }
        [HttpPost]
        //public async Task<IActionResult> Index(PrimaryKeyDTO dto)
        public IActionResult Index(PrimaryKeyDTO dto)
        {
            DPDPackageObjServicesClient client = new DPDPackageObjServicesClient();

            //utworzenie obiektu autoryzacyjnego
            authDataV1 authData = new authDataV1()
            {
                masterFidSpecified = true,
                masterFid = 1495,
                login = "test",
                password = "thetu4Ee"
            };

            //pobieranie listy przesyłek do wysyłki
            var shippings = ShippingRepository.Select(c => c.TaskId == dto.Id).ToList();
            var shippingsCount = shippings.Count();
            Log.Information("Pobieranie listy paczek do wysyłki zakończone powodzeniem");

            // walidacja danych przesyłek i nadawanie numerów listów przewozowych  
            int FID = 1495;
            packageOpenUMLFeV1[] packageArray = new packageOpenUMLFeV1[1]; //ile przesyłek  
            packageOpenUMLFeV1 package = new packageOpenUMLFeV1()
            {
                parcels = new parcelOpenUMLFeV1[shippings.Count], //ile paczek 
                payerType = payerTypeEnumOpenUMLFeV1.SENDER,
                payerTypeSpecified = true,
                thirdPartyFIDSpecified = true
            };

            for (int i = 0; i < 1; i++)
            {
                var przesylka = shippings[i];

                //pobranie i przypisanie watrosci nadawcy 
                var nadawca = LocationsRepository.Single(c => c.Id == przesylka.SenderLocationId);
                packageAddressOpenUMLFeV1 daneNadawcy = new packageAddressOpenUMLFeV1()
                {
                    address = nadawca.Street + nadawca.StreetNumber + nadawca.ApartmentNumber,
                    city = nadawca.City,
                    company = nadawca.CompanyName,
                    countryCode = nadawca.CountryISO,
                    fidSpecified = true,
                    postalCode = nadawca.Zipcode,
                    fid = FID
                };
                package.sender = daneNadawcy;
                Log.Information("Pobrano i przypisano wartości nadawcy");

                //pobranie i przypisanie wartości odbiorcy 
                var odbiorca = LocationsRepository.Single(c => c.Id == przesylka.RecipientLocationId);
                packageAddressOpenUMLFeV1 daneOdbiorcy = new packageAddressOpenUMLFeV1()
                {
                    address = odbiorca.Street + odbiorca.StreetNumber + odbiorca.ApartmentNumber,
                    city = odbiorca.City,
                    company = odbiorca.CompanyName,
                    countryCode = odbiorca.CountryISO,
                    fidSpecified = true,
                    postalCode = odbiorca.Zipcode,
                    fid = FID
                };
                package.receiver = daneOdbiorcy;
                Log.Information("Pobrano i przypisano wartości odbiorcy");

                //rodzaj przesyłki 
                servicesOpenUMLFeV2 services = new servicesOpenUMLFeV2();
                serviceCODOpenUMLFeV1 cod = new serviceCODOpenUMLFeV1()
                {
                    currency = serviceCurrencyEnum.PLN,
                    currencySpecified = true,
                    amount = "0"
                };
                services.cod = cod;
                package.services = services;

                Dictionary<int, int> shippingsId = new Dictionary<int, int>();
                for (int p = 0; p < shippingsCount; p++)
                {
                    var id = shippings[p];
                    shippingsId.Add(p, id.Id);
                }

                //doadać dane paczki
                for (int t = 0; t < shippingsCount; t++)
                {
                    var id = shippingsId[t];

                    //pobieranie paczek do wysyłki        
                    var shippingPackage = ShippingPackageRepository.Single(c => c.ShippingId == id);
                    package.parcels[t] = (new parcelOpenUMLFeV1()
                    {
                        sizeX = shippingPackage.SizeX,
                        sizeXSpecified = true,
                        sizeY = shippingPackage.SizeY,
                        sizeYSpecified = true,
                        sizeZ = shippingPackage.SizeZ,
                        sizeZSpecified = true,
                        weight = (double)shippingPackage.Weight,
                        weightSpecified = true,
                        content = shippingPackage.Description,
                        customerData1 = "dane1"
                    });
                }
                Log.Information("Przypisano dane do paczek");
            }
            packageArray[0] = package;

            packagesGenerationResponseV1 dpd = client.generatePackagesNumbersV1(packageArray, pkgNumsGenerationPolicyV1.IGNORE_ERRORS, authData);

            // interpretacja wyniku
            long sessionId = dpd.sessionId;
            long packageId = dpd.packages[0].packageId;
            string umlfStatus = dpd.status.ToString(); 
            // status całej sesji
            //long parcelId = dpd.packages[0].parcels[0].parcelId;
            //string waybill = dpd.packages[0].parcels[0].waybill;

            Log.Information("Status sesji: " + umlfStatus);  // statusy poszczególnych 
            foreach ( packagePGRV1 pkgs in dpd.packages ) 
            { 
                Log.Information("Status package: " + pkgs.status.ToString());
                foreach ( parcelPGRV1 parcel in pkgs.parcels )
                {
                    Log.Information("Status parcel: " + parcel.status.ToString());
                } 
            }

            // Tworzenie etykiet 
            //sessionDSPV1 session = new sessionDSPV1();
            dpdServicesParamsV1 param = new dpdServicesParamsV1()
            {
                policy = policyDSPEnumV1.STOP_ON_FIRST_ERROR,
                policySpecified = true
            };               

            // Na podstawie sessionId
            sessionDSPV1 session = new sessionDSPV1()
            {
                sessionId = sessionId,
                sessionIdSpecified = true,
                sessionType = sessionTypeDSPEnumV1.DOMESTIC,
                sessionTypeSpecified = true,
            };             
            param.session = session;

            documentGenerationResponseV1 ret = client.generateSpedLabelsV1(param, outputDocFormatDSPEnumV1.PDF, outputDocPageFormatDSPEnumV1.A4, authData); 
                
            //zapisanie danych w bazie
            for(int i = 0; i < dpd.packages[0].parcels.Length; i++)
            {
                var paczka = dpd.packages[0].parcels[i];
                var poco = new ShippingPackagePOCO()
                {
                    Weight = (decimal)package.parcels[i].weight,
                    SizeX = package.parcels[i].sizeX,
                    SizeY = package.parcels[i].sizeY,
                    SizeZ = package.parcels[i].sizeZ,
                    //trzeba zaktualizować ShippingPackagePOCO bo nie ma tam tych danych
                    //WaynillNo = paczka.waybill,
                    //DPDId = paczka.parcelId,
                    //DPDStatus = paczka.status,
                };
                ShippingPackageRepository.Add(poco);
                ShippingPackageRepository.Save();
            }
            return Ok();            
        }
    }
}