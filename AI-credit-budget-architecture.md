# AI Credit Budget Automation Architecture

This document describes a proposed Power Platform architecture for requesting,
approving, applying, and reconciling individual GitHub Copilot AI credit
budgets.

## 1. Solution architecture

```mermaid
flowchart LR
    subgraph Users[People]
        Requester[Requester]
        Manager[Manager]
        Admin[Copilot Administrator]
    end

    subgraph PowerPlatform[Microsoft Power Platform]
        Form[Microsoft Forms Intake]
        Intake[Power Automate Intake Flow]
        Approval[Power Automate Approval Flow]
        Fulfillment[Power Automate Fulfillment Flow]
        Reconcile[Scheduled Reconciliation Flow]
        Notify[Notification Flow]
    end

    subgraph Data[Control and Audit Data]
        Requests[(SharePoint Request List)]
        Config[(Environment Variables)]
        Secrets[Secure Connection / Secret Store]
        Audit[(Audit and Run History)]
    end

    subgraph GitHub[GitHub Enterprise Cloud]
        BudgetAPI[Billing Budgets REST API]
        Budgets[(Universal, Cost Center, and Individual Budgets)]
    end

    Requester -->|Submit request| Form
    Form -->|New response| Intake
    Intake -->|Create pending request| Requests
    Intake -->|Request approval| Approval
    Approval <-->|Approve / reject| Manager
    Approval -->|Approved request ID| Fulfillment
    Fulfillment -->|Read approved state| Requests
    Fulfillment -->|Read configuration| Config
    Fulfillment -->|Authenticate| Secrets
    Fulfillment -->|GET / POST / PATCH| BudgetAPI
    BudgetAPI --> Budgets
    Fulfillment -->|Result and prior/new state| Audit
    Fulfillment -->|Update status| Requests
    Fulfillment --> Notify
    Notify -->|Status| Requester
    Notify -->|Failure / drift| Admin
    Reconcile -->|Read desired state| Requests
    Reconcile -->|Read actual state| BudgetAPI
    Reconcile -->|Record drift| Audit
    Reconcile --> Notify
```

### Component responsibilities

| Component | Responsibility |
|---|---|
| Microsoft Forms | Collects the initial request |
| Intake flow | Validates fields and creates a tracked request |
| SharePoint list | Stores desired state, approval status, and processing result |
| Approval flow | Routes each valid request to the requester's manager |
| Fulfillment flow | Creates or updates the GitHub individual budget |
| Reconciliation flow | Compares approved requests with GitHub's actual state |
| Notification flow | Sends success, rejection, failure, and expiration notices |
| Environment variables | Store non-secret configuration |
| Secure connection | Protects the GitHub API credential |
| Audit history | Records previous state, resulting state, actor, and outcome |

## 2. Request and approval flow

```mermaid
flowchart TD
    Start([User submits budget request]) --> Capture[Capture user, total USD budget, justification, cost center, and expiration]
    Capture --> Validate{Request valid?}

    Validate -->|No| Reject[Record rejection reason]
    Reject --> NotifyReject[Notify requester]
    NotifyReject --> StopReject([Request closed])

    Validate -->|Yes| Save[Create request with unique ID]
    Save --> ManagerApproval[Request manager approval]
    ManagerApproval --> Decision{Approved?}

    Decision -->|No| Declined[Record declined status]
    Declined --> NotifyReject

    Decision -->|Yes| Duplicate{Already fulfilled?}
    Duplicate -->|Yes| NoChange[Return existing result]
    Duplicate -->|No| ReadCurrent[Read current GitHub budget]

    ReadCurrent --> Compare{Desired state equals actual state?}
    Compare -->|Yes| NoChange
    Compare -->|No| Operation{Individual budget exists?}

    Operation -->|No| Create[Create user budget]
    Operation -->|Yes| Update[Update user budget]

    Create --> Verify[Read back and verify]
    Update --> Verify

    Verify --> Success{Verified?}
    Success -->|No| Failure[Record failure and alert administrator]
    Success -->|Yes| Complete[Record budget ID, previous state, new state, and expiration]

    NoChange --> Complete
    Complete --> NotifySuccess[Notify user and requester]
    NotifySuccess --> EndSuccess([Request completed])
    Failure --> EndFailure([Manual investigation required])
```

Every valid request requires one approval from the requester's manager. The
requested budget amount does not change the approval route.

## 3. Request fulfillment sequence

```mermaid
sequenceDiagram
    autonumber
    actor Requester
    participant Forms as Microsoft Forms
    participant Intake as Intake Flow
    participant Store as SharePoint Requests
    participant Approval as Approval Flow
    actor Manager
    participant Fulfill as Fulfillment Flow
    participant GitHub as GitHub Budgets API
    participant Notify as Notification Flow

    Requester->>Forms: Submit requested total budget and justification
    Forms->>Intake: Trigger with response ID
    Intake->>Forms: Get response details
    Intake->>Intake: Validate fields and identify requester's manager
    Intake->>Store: Create request with unique ID and Pending status
    Intake->>Approval: Start approval with request ID
    Approval->>Manager: Send approval request

    alt Request rejected
        Manager-->>Approval: Reject with comments
        Approval->>Store: Set status to Rejected
        Approval->>Notify: Send rejection result
        Notify-->>Requester: Request rejected with reason
    else Request approved
        Manager-->>Approval: Approve
        Approval->>Store: Store approver and approval timestamp
        Approval->>Fulfill: Start fulfillment with request ID
        Fulfill->>Store: Read approved request and prior processing status

        alt Request already fulfilled
            Store-->>Fulfill: Existing successful result
            Fulfill->>Notify: Reuse recorded result without another write
        else Request requires processing
            Fulfill->>GitHub: GET current user budget
            GitHub-->>Fulfill: Current budget or not found

            alt No change required
                Fulfill->>Store: Record No Change status
            else Create individual budget
                Fulfill->>GitHub: POST user-scoped AI credit budget
                GitHub-->>Fulfill: Created budget ID
                Fulfill->>GitHub: GET budget to verify
                GitHub-->>Fulfill: Verified resulting state
                Fulfill->>Store: Record prior and resulting state
            else Update individual budget
                Fulfill->>GitHub: PATCH amount and expiration
                GitHub-->>Fulfill: Updated budget
                Fulfill->>GitHub: GET budget to verify
                GitHub-->>Fulfill: Verified resulting state
                Fulfill->>Store: Record prior and resulting state
            end

            Fulfill->>Notify: Send fulfillment result
        end

        Notify-->>Requester: Approved budget applied or no change required
    end
```

## 4. Suggested implementation phases

### Phase 1: Intake and approval

- Microsoft Forms
- SharePoint request list
- Intake validation flow
- Approval flow
- Approval and rejection notifications
- Manual GitHub fulfillment by an administrator

The administrator records the GitHub budget ID and result in SharePoint.

### Phase 2: Automated fulfillment

- GitHub REST API custom connector
- Current-budget lookup
- Automated budget creation and updates
- Read-after-write verification
- Idempotency controls
- Failure notifications

### Phase 3: Governance

- Scheduled reconciliation
- Expiration reminders
- Drift reporting
- Operational dashboard
- Cost-center reporting
- Controlled automatic cleanup

This phased approach allows AVEVA to deliver the intake and approval process
without waiting for GitHub API authentication and production automation to be
completed.
