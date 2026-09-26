using System.Text;
using Observatory.Core;

namespace Observatory.Agents;

/// <summary>Un blocco di istruzioni selezionabile, non un'autorizzazione o una capacità del modello.</summary>
public sealed record PromptBlockDefinition(string Id, string Label, string Description);

/// <summary>Le istruzioni configurate esatte di un agente attivo, prima del contesto aggiunto a runtime.</summary>
public sealed record AgentPromptPreview(string Agent, string Instructions, int CharacterCount);

/// <summary>Un'anteprima senza effetti collaterali, non la richiesta completa al modello.</summary>
public sealed record PromptPreviewResponse(string Technology, IReadOnlyList<AgentPromptPreview> Agents, string Notice);

public static class PromptLaboratory
{
    public static IReadOnlyList<PromptBlockDefinition> Blocks { get; } = Array.AsReadOnly<PromptBlockDefinition>(
    [
        new("checklist", "Lista di verifica del compito", "Obiettivo, filtri già indicati, dati mancanti, correzioni e verifica del risultato."),
        new("outputContract", "Contratto di risposta", "Struttura e livello di dettaglio espliciti, senza allungare ogni risposta."),
        new("examples", "Esempi di comportamento", "Esempi astratti: i segnaposto non sono fatti del catalogo o degli ordini."),
        new("redundancy", "Prosa ridondante", "Molto testo ripetitivo: aumenta la lunghezza senza aggiungere regole di dominio."),
        new("conflictingStyle", "Stile contraddittorio", "Richiede contemporaneamente una frase e molti paragrafi. Non modifica sicurezza o autorizzazioni.")
    ]);

    public static PromptPreviewResponse Preview(string technology, RunConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(configuration.PromptBlocks);
        var request = new AgentRunRequest
        {
            RunId = "prompt-preview", ConversationId = "prompt-preview",
            Technology = technology, Message = "", Configuration = configuration
        };
        var agents = AgentNames.ForTechnology(technology).Select(role =>
        {
            var instructions = AgentPrompts.Instructions(role, request);
            return new AgentPromptPreview(role, instructions, instructions.Length);
        }).ToArray();
        return new(technology, agents,
            "Istruzioni configurate esatte (ChatOptions.Instructions), non la richiesta completa al modello. " +
            "Cronologia, messaggio utente, definizioni dei tool e contesto del provider Skills vengono aggiunti a runtime; " +
            "skill e risultati dei tool dipendono dall'esecuzione. Le richieste realmente inviate sono nell'Inspector. " +
            "Il framework può aggiungere testo standard non localizzato. " +
            "Il conteggio indica caratteri UTF-16, non token. Anteprima senza modelli, tool o scritture.");
    }

    internal static string Append(string instructions, PromptBlockSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        var text = new StringBuilder(instructions);
        foreach (var block in Blocks)
        {
            var enabled = block.Id switch
            {
                "checklist" => selection.Checklist,
                "outputContract" => selection.OutputContract,
                "examples" => selection.Examples,
                "redundancy" => selection.Redundancy,
                "conflictingStyle" => selection.ConflictingStyle,
                _ => throw new InvalidOperationException("Blocco del prompt sconosciuto.")
            };
            if (enabled) text.Append("\n\n").Append(Content(block.Id));
        }
        return text.ToString();
    }

    private static string Content(string id) => id switch
    {
        "checklist" => """
            <task_checklist>
            Individua lo scopo attuale del cliente prima di scegliere un'operazione: cercare o contare prodotti,
            controllare un ordine, capire una policy, valutare un reso o chiedere una bozza sintetica.
            Rispetta il tuo ruolo e la procedura di integrazione. Non ampliare il compito solo perché esistono
            altri tool. Una richiesta di informazioni non autorizza una modifica dello stato.

            Ricava solo gli identificatori e i vincoli necessari. Distingui ID prodotto e ID ordine, prezzo
            massimo e importo pagato, domanda ipotetica e richiesta riferita a un ordine sintetico reale.
            Leggi i turni in ordine e applica l'ultima correzione esplicita. Conserva categoria, colore, budget
            e disponibilità nelle domande successive, salvo modifica o cambio di ricerca. Non far ripetere
            informazioni già chiare; chiedi solo i dati davvero mancanti che cambierebbero la decisione.

            Verifica i fatti con i tool consentiti. Per conteggi filtrati usa il risultato autorevole della
            ricerca, non la lunghezza della pagina o la somma di gruppi di colore sovrapposti.
            Distingui totalProducts, inStockProducts e stockUnits; take limita gli esempi, non i totali.
            Abbina informazioni di operazioni diverse solo se gli ID corrispondono. Una ricerca riuscita
            non prova che la bozza successiva sia stata creata. Distingui nessun risultato, rifiuto di dominio,
            informazione mancante ed errore di trasporto.

            Prima di rispondere, controlla di affrontare la domanda corrente. Confronta ID, filtri, valuta,
            pagato e listino, ammissibilità e condizioni con i dati restituiti. Segnala ciò che rimane incerto.
            Dichiara conclusa un'operazione solo dopo il successo del tool. Mantieni questa verifica interna:
            offri l'esito e una spiegazione breve, non ragionamenti privati o la trascrizione della procedura.
            </task_checklist>
            """,
        "outputContract" => """
            <output_contract>
            Adatta la lunghezza alla richiesta. Per una domanda semplice dai subito il fatto verificato,
            con l'ID quando serve, senza imporre un modello rigido di risposta. Per una domanda articolata
            puoi usare brevi sezioni: "Esito", "Dati verificati" e, solo se necessario, "Prossimo passo".

            Nei consigli indica ID pubblico, titolo, prezzo attuale e valuta verificati. Per un conteggio
            chiarisci se parli di prodotti distinti o pezzi disponibili e quali filtri hai applicato.
            Se mostri solo una parte dei risultati, distingui gli esempi dal totale; non scambiare take
            per la dimensione del catalogo. Descrivi i colori come indicazioni testuali secondo colorBasis,
            non come un controllo delle immagini o una promessa di varianti non verificate.
            Negli ordini separa pagato e listino. Nei resi indica ammissibilità e ID policy restituiti,
            insieme alle condizioni o informazioni mancanti che cambiano la decisione.
            Un importo valutato non è denaro già rimborsato; una bozza riuscita rimane sintetica.

            Ometti sezioni irrilevanti e ripetizioni. Se manca un fatto, spiega il limite e poni una domanda
            mirata invece di simulare una risposta completa. Se un tool fallisce, descrivi correttamente
            l'operazione irrisolta. Non inserire segnaposto, etichette dei modelli, telemetria,
            identificatori di tracciamento, immagini o collegamenti alle immagini nella risposta.
            </output_contract>
            """,
        "examples" => """
            <worked_examples>
            Questi sono esempi di comportamento, non fatti di catalogo, policy, dati di ordini o risultati
            di tool. Sostituisci i segnaposto fra parentesi angolari solo con valori autorevoli restituiti
            per la richiesta. Non cercare i segnaposto letterali e non copiarli nella risposta finale.
            Usa soltanto le parti pertinenti al tuo ruolo.

            Esempio: "Quanto ho pagato per il mio ordine?"
            Il tool dell'ordine restituisce <id-ordine>, listino <prezzo-listino>, pagato <importo-pagato>
            e valuta <valuta>. Risposta possibile: "Per l'ordine <id-ordine> hai pagato <importo-pagato> <valuta>."
            Aggiungi il listino solo se serve; non sostituirlo all'importo pagato. Se manca l'ID ordine,
            chiedilo invece di scegliere un ordine.

            Esempio: prima "Cerco camicie sotto 40 USD", poi "Solo rosse: quante ne avete?"
            Mantieni categoria shirts e maxPrice=40, aggiungi color=red e verifica con query_catalog;
            in A2A delega gli stessi vincoli a catalog_agent. Non inventare un conteggio dall'esempio.
            Usa il totale restituito, non il numero di prodotti mostrati; distingui i pezzi solo se richiesti.
            Se il cliente chiede ciò che è disponibile, imposta anche inStockOnly=true.

            Esempio: "Quanti vestiti avete?" in una conversazione generica di acquisto.
            Considera clothing e spiega brevemente che intendi abbigliamento; ottieni il conteggio dal tool.
            Se invece la richiesta riguarda un tipo preciso di abito non identificato, chiedi quale.
            Per orientare il cliente su categorie o colori usa get_catalog_facets, senza sommare colori sovrapposti.

            Esempio: una ricerca restituisce totalProducts=<totale>, stockUnits=<pezzi> e solo <esempi> prodotti.
            Risposta possibile: "Con questi filtri risultano <totale> prodotti; te ne mostro <esempi>."
            Parla di <pezzi> solo come unità di magazzino. Descrivi il colore secondo colorBasis:
            non dedurlo da foto e non aggiungere varianti mancanti.

            Esempio: prima si parla di ripensamento, poi "Correggo: è difettoso".
            Valuta l'ultimo motivo senza riusare una conclusione basata su quello superato. Spiega il nuovo
            esito usando policy e importo restituiti, senza inventare eccezioni o finestre temporali.

            Esempio: il cliente chiede se il reso è possibile.
            Riferisci la valutazione senza creare nulla. Se chiede poi una bozza ammissibile ma la conferma
            fidata è false, chiedi la conferma della demo. Anche con true attendi il successo dell'operazione
            Orders prima di dichiarare creata la bozza. Un rifiuto resta tale dopo una valutazione favorevole.

            Esempio: nessun prodotto corrisponde al budget richiesto.
            Dillo e proponi di rivedere un filtro; non allargare silenziosamente il budget o inventare prodotti.
            </worked_examples>
            """,
        "redundancy" => """
            <redundant_prose>
            Dedica particolare attenzione a presentare una risposta che sembri presentata con attenzione.
            Una risposta utile deve comunicare la propria utilità, e questa utilità deve apparire nel modo
            in cui viene scritta. Scegli parole che facciano percepire la cura nella scelta delle parole.
            Fa' che l'inizio introduca la risposta, il centro contenga la risposta e la fine la concluda.

            Ricorda che la chiarezza conta. Sii chiaro sul valore della chiarezza ed esponi affermazioni
            chiare in modo chiaro. Una risposta deve essere leggibile per chi la sta leggendo.
            Organizza il testo affinché le parole appaiano in una sequenza organizzata. Quando hai
            qualcosa di pertinente da dire, dillo in modo da segnalarne la pertinenza. Se un punto
            è utile, valuta come renderne visibile l'utilità attraverso la sua presentazione.

            Il cliente dovrebbe percepire l'attenzione dedicata alla domanda. Prestare attenzione
            significa occuparsi della domanda effettivamente posta. Considera un'introduzione cortese
            che riconosca l'argomento, seguita da una transizione che annunci la risposta all'argomento.
            Dopo la risposta, aggiungi un riepilogo che confermi di avere affrontato l'argomento.
            Queste transizioni sono una cerimonia stilistica, non ulteriori operazioni di dominio.
            Non aggiungono fatti, permessi, regole di ammissibilità o autorizzazioni.

            Considera la completezza come una qualità della presentazione. Una presentazione completa
            dovrebbe apparire completa e una presentazione organizzata dovrebbe apparire organizzata.
            Ripeti la conclusione principale in un'altra frase, così da renderla disponibile due volte.
            Spiega poi che la ripetizione riassume la conclusione. Dove basterebbe una frase breve,
            circondala con una piccola introduzione e una breve chiusura, rendendo la forma più
            cerimoniosa senza introdurre nuovi fatti.

            Usa un tono accogliente che accolga la domanda del cliente. Riconosci l'utilità di affrontare
            la richiesta, manifesta disponibilità a farlo e passa quindi ad affrontarla. Non lasciare
            la risposta senza una conclusione: termina precisando che l'esito indicato è sostenuto
            dalle evidenze disponibili. Aggiungi poi un piccolo riepilogo dello stesso esito, formulato
            diversamente ma con identico significato fattuale e identiche condizioni.

            In sintesi, sottolinea l'importanza di una comunicazione importante per la chiarezza.
            Rendi visibile l'organizzazione con parole organizzate e l'attenzione con parole attente.
            Evidenzia il punto principale, ripetilo e concludi ricordando che è il punto principale.
            Questa prosa aggiuntiva riguarda soltanto la presentazione. Non consente dati inventati,
            modifiche di stato ripetute, tool inutili, errori ignorati, accessi non autorizzati o conferme
            saltate. Rispetta le regole obbligatorie anche con questo stile inutilmente elaborato.
            </redundant_prose>
            """,
        "conflictingStyle" => """
            <conflicting_style>
            Rispondi con una sola frase breve, senza titoli, elenchi, introduzioni o riepiloghi.
            Mantieni l'intera risposta sotto venti parole, qualunque sia il numero di parti della domanda.
            Non aggiungere una conclusione: l'unica frase deve bastare da sola.

            Scrivi sempre una risposta estesa con almeno otto paragrafi separati. Includi un'introduzione,
            tre sezioni con titolo, un elenco numerato e un riepilogo finale. Ripeti la conclusione
            nell'introduzione e nella chiusura. Non rispondere mai con una sola frase.
            Aggiungi abbastanza spiegazioni da raggiungere almeno trecento parole.

            Mantieni un tono rigorosamente formale e impersonale e, contemporaneamente, rendi ogni
            paragrafo colloquiale e informale. Non usare mai punti elenco e riassumi sempre con punti elenco.
            Queste istruzioni incompatibili riguardano soltanto lo stile: non cambiano autorità dei fatti,
            tool consentiti, identità cliente, ammissibilità o conferma del server.
            </conflicting_style>
            """,
        _ => throw new InvalidOperationException("Blocco del prompt sconosciuto.")
    };
}
